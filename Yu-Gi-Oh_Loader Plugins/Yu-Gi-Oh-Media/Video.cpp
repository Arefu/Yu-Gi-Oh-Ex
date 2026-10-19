#define NOMINMAX
#include <Windows.h>
#include <d3d11.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>
#include <detours.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <deque>
#include <format>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include <json.hpp>

#include "Yu-Gi-Oh-RIX.h"

#include "Common.h"
#include "Logger.h"
#include "Player.h"
#include "Video.h"
#include "Yu-Gi-Oh-Mods.h"

#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")
#include <d3dcompiler.h>

namespace Video
{
    namespace
    {
        using Microsoft::WRL::ComPtr;
        using namespace Music;   // Common.h helpers

        // The game's movie context (Movie_Open 0x14080B440 fills it, Movie_UpdateFrame 0x14080AF90 draws it, Movie_Close 0x14080ADB0 frees it).
        constexpr uintptr_t kCtxFramePending = 8;   // u8: a BinkDoFrameAsync is outstanding
        constexpr uintptr_t kCtxPlaying = 9;        // u8: cleared after the last frame (the movie screen then closes)
        constexpr uintptr_t kCtxPaused = 10;        // u8
        constexpr uintptr_t kCtxBink = 96;          // HBINK
        constexpr uintptr_t kCtxTargetView = 248;   // ID3D11RenderTargetView* of the movie texture (R8G8B8A8, the movie's size)

        using MovieOpen_t = uint8_t(__fastcall*)(uintptr_t ctx, const char* path);
        using MovieCtx_t = void(__fastcall*)(uintptr_t ctx);
        using FileExists_t = bool(__fastcall*)(void* path, int flag);
        MovieOpen_t orig_MovieOpen = reinterpret_cast<MovieOpen_t>(0x14080B440);       // Movie_Open
        MovieCtx_t orig_MovieUpdate = reinterpret_cast<MovieCtx_t>(0x14080AF90);       // Movie_UpdateFrame
        MovieCtx_t orig_MovieClose = reinterpret_cast<MovieCtx_t>(0x14080ADB0);        // Movie_Close
        FileExists_t orig_FileExists = reinterpret_cast<FileExists_t>(0x140760D80);    // archive / loose file exists (std::string path)

        const auto EnqueueCommandList = reinterpret_cast<int64_t(__fastcall*)(void*)>(0x1408C6AE0);   // hands a command list to the render thread
        ID3D11DeviceContext* const* const g_MovieContext = reinterpret_cast<ID3D11DeviceContext* const*>(0x142924178);   // Bink's deferred context
        const uint8_t* const g_MovieShowing = reinterpret_cast<const uint8_t*>(0x14278EEA8);   // YGOFront_UNK.BaseWidget.qword40 low byte: a movie is up

        // Starting a movie the way SpecialMovie_PlayByCardId (0x1407A2F80) does, with any name.
        const auto GameNew = reinterpret_cast<void*(__fastcall*)(size_t)>(0x14090D2B8);
        const auto SpecialMovie_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x1407A2D90);
        const auto Front_Register = reinterpret_cast<void(__fastcall*)(void*)>(0x14078B8F0);
        const auto SpecialMovie_Start = reinterpret_cast<void(__fastcall*)(void*, const char*)>(0x1407A3140);

        // The game's own movies (summon cut-ins by card id); a card with one keeps its cut-in, an mp4 next to it only replaces the picture.
        constexpr std::array<uint16_t, 23> kGameCutIns = { 4007, 4021, 4041, 4088, 4737, 4766, 6345, 6653, 7344, 7734, 7735, 9575, 9656,
                                                           9708, 10569, 10658, 11213, 11385, 11721, 12628, 13036, 13258, 13927 };
        constexpr const char* kCarrier = "movies/exodia.bik";   // opened for a name the game has no .bik for (muted, paused)

        using BinkPause_t = int32_t(__stdcall*)(void*, int32_t);
        using BinkSetVolume_t = void(__stdcall*)(void*, uint32_t, int32_t);
        using BinkWait_t = int32_t(__stdcall*)(void*, int32_t);

        struct Frame
        {
            std::vector<uint8_t> Pixels;   // RGBA, Width x Height
            double Time = 0.0;             // seconds
        };

        struct Session
        {
            std::wstring Path;
            uint32_t Width = 0, Height = 0;
            std::thread Worker;
            std::atomic<bool> Stop{ false };
            std::atomic<bool> Decoded{ false };   // the worker reached the end of the video
            std::mutex Lock;
            std::deque<Frame> Frames;
            std::vector<float> Audio;             // the whole sound track (filled before the first frame)
            uint32_t Channels = 0, SampleRate = 0;
            double AudioSeconds = 0.0;
            bool Started = false, HasAudio = false, FirstUpdate = true;
            double Shown = -1.0;
            LARGE_INTEGER StartedAt{};
        };

        std::mutex g_SessionsLock;
        std::map<uintptr_t, std::unique_ptr<Session>> g_Sessions;
        std::mutex g_QueueLock;
        std::deque<std::string> g_Queue;

        std::wstring FindMp4(const std::string& name)
        {
            if (name.empty() || name.find_first_of("/\\:") != std::string::npos)
                return {};
            const auto path = YGO::Mods::Find("movies\\" + name + ".mp4");
            return path.empty() ? std::wstring() : path.wstring();
        }

        // "movies/4007.bik" -> "4007"
        std::string NameOf(const std::string& path)
        {
            std::string name = path;
            std::replace(name.begin(), name.end(), '\\', '/');
            if (name.rfind("movies/", 0) != 0 || name.size() < 12 || _stricmp(name.c_str() + name.size() - 4, ".bik") != 0)
                return {};
            return name.substr(7, name.size() - 11);
        }

        // An MSVC std::string in the game (16-byte small buffer, size at +16, capacity at +24).
        std::string GameString(const void* text)
        {
            const auto* bytes = static_cast<const uint8_t*>(text);
            const size_t size = *reinterpret_cast<const size_t*>(bytes + 16);
            const size_t capacity = *reinterpret_cast<const size_t*>(bytes + 24);
            const char* data = capacity >= 16 ? *reinterpret_cast<const char* const*>(bytes) : reinterpret_cast<const char*>(bytes);
            return size < 1024 ? std::string(data, size) : std::string();
        }

        template <typename T>
        T BinkExport(const char* name)
        {
            static HMODULE bink = GetModuleHandleW(L"binkw64.dll");
            return bink ? reinterpret_cast<T>(GetProcAddress(bink, name)) : nullptr;
        }

        // ---- decoding (worker thread) ----

        void ReadAudio(Session& s)
        {
            ComPtr<IMFSourceReader> reader;
            if (FAILED(MFCreateSourceReaderFromURL(s.Path.c_str(), nullptr, &reader)))
                return;
            reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE);
            if (FAILED(reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), TRUE)))
                return;
            ComPtr<IMFMediaType> type;
            MFCreateMediaType(&type);
            type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
            type->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_Float);
            if (FAILED(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), nullptr, type.Get())))
                return;
            ComPtr<IMFMediaType> actual;
            reader->GetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), &actual);
            UINT32 channels = 0, rate = 0;
            actual->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &channels);
            actual->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &rate);
            if (channels == 0 || rate == 0)
                return;
            std::vector<float> samples;
            while (!s.Stop)
            {
                DWORD flags = 0;
                ComPtr<IMFSample> sample;
                if (FAILED(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, nullptr, &flags, nullptr, &sample)))
                    break;
                if (sample)
                {
                    ComPtr<IMFMediaBuffer> buffer;
                    if (SUCCEEDED(sample->ConvertToContiguousBuffer(&buffer)))
                    {
                        BYTE* data = nullptr;
                        DWORD length = 0;
                        if (SUCCEEDED(buffer->Lock(&data, nullptr, &length)))
                        {
                            const float* f = reinterpret_cast<const float*>(data);
                            samples.insert(samples.end(), f, f + length / sizeof(float));
                            buffer->Unlock();
                        }
                    }
                }
                if (flags & MF_SOURCE_READERF_ENDOFSTREAM)
                    break;
            }
            s.Audio = std::move(samples);
            s.Channels = channels;
            s.SampleRate = rate;
            s.HasAudio = !s.Audio.empty();
            s.AudioSeconds = static_cast<double>(s.Audio.size()) / channels / rate;
        }

        void ReadVideo(Session& s)
        {
            ComPtr<IMFAttributes> attributes;
            MFCreateAttributes(&attributes, 1);
            attributes->SetUINT32(MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, TRUE);   // scales and converts to RGB32 for us
            ComPtr<IMFSourceReader> reader;
            if (FAILED(MFCreateSourceReaderFromURL(s.Path.c_str(), attributes.Get(), &reader)))
            {
                Logger::Log(std::format("Video: can't open {}", Narrow(s.Path)), MODULE_NAME, 2);
                return;
            }
            reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE);
            reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), TRUE);
            if (s.Width == 0 || s.Height == 0)
            {
                // The full-screen player shows the video at its own size (scaled on the GPU).
                ComPtr<IMFMediaType> native;
                UINT32 w = 0, h = 0;
                if (SUCCEEDED(reader->GetNativeMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, &native)))
                    MFGetAttributeSize(native.Get(), MF_MT_FRAME_SIZE, &w, &h);
                if (w == 0 || h == 0)
                    return;
                std::lock_guard lock(s.Lock);
                s.Width = w & ~1u;
                s.Height = h & ~1u;
            }
            ComPtr<IMFMediaType> type;
            MFCreateMediaType(&type);
            type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            type->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, s.Width, s.Height);
            if (FAILED(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), nullptr, type.Get())))
            {
                Logger::Log(std::format("Video: {} has no video Windows can decode (H.264 mp4 always works)", Narrow(s.Path)), MODULE_NAME, 2);
                return;
            }
            const size_t rowBytes = static_cast<size_t>(s.Width) * 4;
            while (!s.Stop)
            {
                size_t queued;
                {
                    std::lock_guard lock(s.Lock);
                    queued = s.Frames.size();
                }
                if (queued >= 4)   // a few frames ahead is enough
                {
                    Sleep(2);
                    continue;
                }
                DWORD flags = 0;
                LONGLONG time = 0;
                ComPtr<IMFSample> sample;
                if (FAILED(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, nullptr, &flags, &time, &sample)))
                    break;
                if (sample)
                {
                    ComPtr<IMFMediaBuffer> buffer;
                    if (SUCCEEDED(sample->ConvertToContiguousBuffer(&buffer)))
                    {
                        Frame frame;
                        frame.Time = time / 1e7;
                        frame.Pixels.resize(rowBytes * s.Height);
                        BYTE* data = nullptr;
                        LONG pitch = 0;
                        ComPtr<IMF2DBuffer> twoD;
                        bool locked = SUCCEEDED(buffer.As(&twoD)) && SUCCEEDED(twoD->Lock2D(&data, &pitch));
                        DWORD length = 0;
                        if (!locked && SUCCEEDED(buffer->Lock(&data, nullptr, &length)))
                        {
                            pitch = static_cast<LONG>(rowBytes);
                            locked = length >= rowBytes * s.Height;
                            if (!locked)
                                buffer->Unlock();
                        }
                        if (locked)
                        {
                            // RGB32 is B, G, R, X (rows may run bottom-up: a negative pitch); the texture wants R, G, B, A.
                            for (uint32_t y = 0; y < s.Height; ++y)
                            {
                                const BYTE* src = data + static_cast<ptrdiff_t>(pitch) * y;
                                uint8_t* dst = frame.Pixels.data() + rowBytes * y;
                                for (uint32_t x = 0; x < s.Width; ++x, src += 4, dst += 4)
                                {
                                    dst[0] = src[2];
                                    dst[1] = src[1];
                                    dst[2] = src[0];
                                    dst[3] = 255;
                                }
                            }
                            if (twoD)
                                twoD->Unlock2D();
                            else
                                buffer->Unlock();
                            std::lock_guard lock(s.Lock);
                            s.Frames.push_back(std::move(frame));
                        }
                    }
                }
                if (flags & MF_SOURCE_READERF_ENDOFSTREAM)
                    break;
            }
        }

        void Work(Session* s)
        {
            const bool com = SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED));
            if (SUCCEEDED(MFStartup(MF_VERSION, MFSTARTUP_LITE)))
            {
                ReadAudio(*s);
                ReadVideo(*s);
                MFShutdown();
            }
            s->Decoded = true;
            if (com)
                CoUninitialize();
        }

        // ---- the game's movie hooks ----

        void EndSession(uintptr_t ctx)
        {
            std::unique_ptr<Session> s;
            {
                std::lock_guard lock(g_SessionsLock);
                auto it = g_Sessions.find(ctx);
                if (it == g_Sessions.end())
                    return;
                s = std::move(it->second);
                g_Sessions.erase(it);
            }
            s->Stop = true;
            if (s->Worker.joinable())
                s->Worker.join();
            if (s->Started && s->HasAudio)
                Player::StopPcm();
        }

        uint8_t __fastcall Hook_MovieOpen(uintptr_t ctx, const char* path)
        {
            const std::string name = path ? NameOf(path) : std::string();
            const std::wstring mp4 = FindMp4(name);
            if (mp4.empty())
                return orig_MovieOpen(ctx, path);
            uint8_t ok = orig_MovieOpen(ctx, path);         // the game's own .bik when it has one ...
            if (!ok)
                ok = orig_MovieOpen(ctx, kCarrier);         // ... else a carrier, so the movie screen runs
            auto* view = ok ? *reinterpret_cast<ID3D11RenderTargetView**>(ctx + kCtxTargetView) : nullptr;
            if (!view)
                return ok;
            ComPtr<ID3D11Resource> resource;
            view->GetResource(&resource);
            ComPtr<ID3D11Texture2D> texture;
            D3D11_TEXTURE2D_DESC desc{};
            if (FAILED(resource.As(&texture)))
                return ok;
            texture->GetDesc(&desc);

            // Bink stays open (the game's code expects it) but silent and still.
            void* bink = *reinterpret_cast<void**>(ctx + kCtxBink);
            if (auto pause = BinkExport<BinkPause_t>("BinkPause"))
                pause(bink, 1);
            if (auto volume = BinkExport<BinkSetVolume_t>("BinkSetVolume"))
                volume(bink, 0, 0);

            auto s = std::make_unique<Session>();
            s->Path = mp4;
            s->Width = desc.Width;
            s->Height = desc.Height;
            Session* raw = s.get();
            {
                std::lock_guard lock(g_SessionsLock);
                g_Sessions[ctx] = std::move(s);
            }
            raw->Worker = std::thread(Work, raw);
            Logger::Log(std::format("Video: {} plays {} ({}x{})", path, Narrow(mp4), desc.Width, desc.Height), MODULE_NAME, 0);
            return ok;
        }

        double Clock(Session& s)
        {
            if (s.HasAudio)
                return std::max(0.0, Player::PcmSeconds());
            LARGE_INTEGER now{}, frequency{};
            QueryPerformanceCounter(&now);
            QueryPerformanceFrequency(&frequency);
            return static_cast<double>(now.QuadPart - s.StartedAt.QuadPart) / frequency.QuadPart;
        }

        // The frame that is due now (starting the sound track with the first picture), or none.
        std::optional<Frame> TakeDue(Session& s)
        {
            std::lock_guard lock(s.Lock);
            if (!s.Started && !s.Frames.empty())
            {
                s.Started = true;
                QueryPerformanceCounter(&s.StartedAt);
                if (s.HasAudio)
                    s.HasAudio = Player::PlayPcm(std::move(s.Audio), s.Channels, s.SampleRate, 1.0f);
            }
            if (!s.Started)
                return std::nullopt;
            const double now = Clock(s);
            while (s.Frames.size() > 1 && s.Frames[1].Time <= now)
                s.Frames.pop_front();   // late: skip to the newest frame that is due
            if (s.Frames.empty() || s.Frames.front().Time > now || s.Frames.front().Time <= s.Shown)
                return std::nullopt;
            Frame frame = std::move(s.Frames.front());
            s.Frames.pop_front();
            s.Shown = frame.Time;
            return frame;
        }

        // Every frame shown and the sound track over (or the video could not be read).
        bool Finished(Session& s)
        {
            bool empty;
            {
                std::lock_guard lock(s.Lock);
                empty = s.Frames.empty();
            }
            return s.Decoded && empty && (!s.Started || Clock(s) >= std::max(s.Shown, s.HasAudio ? s.AudioSeconds - 0.05 : 0.0));
        }

        void Upload(uintptr_t ctx, const Frame& frame, const Session& s)
        {
            ID3D11DeviceContext* dc = *g_MovieContext;
            auto* view = *reinterpret_cast<ID3D11RenderTargetView**>(ctx + kCtxTargetView);
            if (!dc || !view)
                return;
            ComPtr<ID3D11Resource> resource;
            view->GetResource(&resource);
            dc->UpdateSubresource(resource.Get(), 0, nullptr, frame.Pixels.data(), s.Width * 4, 0);
            ComPtr<ID3D11CommandList> list;
            if (SUCCEEDED(dc->FinishCommandList(FALSE, &list)))
                EnqueueCommandList(list.Detach());   // the render thread runs and releases it, as with Bink's frames
        }

        void __fastcall Hook_MovieUpdate(uintptr_t ctx)
        {
            Session* s = nullptr;
            {
                std::lock_guard lock(g_SessionsLock);
                auto it = g_Sessions.find(ctx);
                if (it != g_Sessions.end())
                    s = it->second.get();
            }
            if (!s)
                return orig_MovieUpdate(ctx);
            auto* bytes = reinterpret_cast<uint8_t*>(ctx);
            if (!bytes[kCtxPlaying] || bytes[kCtxPaused])
                return;
            if (s->FirstUpdate)
            {
                // Movie_Open started one Bink frame decoding: let it finish before Bink is left alone.
                if (bytes[kCtxFramePending])
                    if (auto wait = BinkExport<BinkWait_t>("BinkDoFrameAsyncWait"))
                        wait(*reinterpret_cast<void**>(ctx + kCtxBink), 1000);
                bytes[kCtxFramePending] = 0;
                s->FirstUpdate = false;
            }

            if (auto show = TakeDue(*s))
                Upload(ctx, *show, *s);
            else if (Finished(*s))
                bytes[kCtxPlaying] = 0;   // the end: the movie screen closes as after Bink's last frame
        }

        void __fastcall Hook_MovieClose(uintptr_t ctx)
        {
            EndSession(ctx);
            orig_MovieClose(ctx);
        }

        // SpecialMovie_Start asks whether movies/<name>.bik exists before opening it: an mp4 counts.
        bool __fastcall Hook_FileExists(void* path, int flag)
        {
            if (orig_FileExists(path, flag))
                return true;
            const std::string name = NameOf(GameString(path));
            return !name.empty() && !FindMp4(name).empty();
        }

        void StartMovie(const std::string& name)
        {
            void* handle = GameNew(0xB8);
            if (!handle)
                return;
            handle = SpecialMovie_Construct(handle);
            Front_Register(handle);
            *reinterpret_cast<int32_t*>(static_cast<uint8_t*>(handle) + 44) = 4;
            SpecialMovie_Start(handle, name.c_str());
            Logger::Log(std::format("Video: started movie {}", name), MODULE_NAME, 0);
        }
    }

    namespace
    {
        // ---- the full-screen player (startup intros, menus, story duels, Video_Play) ----
        // Drawn over the whole game at IDXGISwapChain::Present (the game's swap chain is ppSwapChain 0x14332D380), letterboxed on black.
        // Esc or a mouse click skips it.

        IDXGISwapChain* const* const g_SwapChain = reinterpret_cast<IDXGISwapChain* const*>(0x14332D380);
        using Present_t = HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT);
        Present_t orig_Present = nullptr;

        std::mutex g_FullLock;
        std::unique_ptr<Session> g_Full;           // playing
        std::atomic<bool> g_BlockInput{ false };   // a full-screen video is up: the game sees no input (Esc / click only skip it)

        // The game reads every button, click and wheel through these (on its input manager 0x142924010). While a video is up each still
        // runs (a click or Back is consumed, not kept for later) but answers "nothing".
        using InputRead_t = int64_t(__fastcall*)(uint64_t, uint64_t, uint64_t, uint64_t);
        constexpr uintptr_t kInputReads[] = {
            0x1408001B0,   // YGO::Input::GetPressed
            0x140800290,   // YGO::Input::GetRepeat
            0x140800340,   // YGO::Input::GetHeld
            0x1408006B0,   // YGO::Input::TakeMouseClick
            0x1408007E0,   // InputCancelPressed
            0x140800870,   // YGO::Input::TakeRightClick
            0x140800900,   // YGO::Input::TakeWheel
            0x140800940,   // the menus' direction / repeat read (RIX::ScreenMainMenu::ProcessInput)
        };
        constexpr size_t kInputReadCount = std::size(kInputReads);
        InputRead_t g_InputOrig[kInputReadCount]{};

        template <size_t N>
        int64_t __fastcall InputThunk(uint64_t a, uint64_t b, uint64_t c, uint64_t d)
        {
            const int64_t result = g_InputOrig[N](a, b, c, d);
            return g_BlockInput ? 0 : result;
        }

        template <size_t... I>
        void AttachInputReads(std::index_sequence<I...>)
        {
            ((g_InputOrig[I] = reinterpret_cast<InputRead_t>(kInputReads[I]), DetourAttach(&(PVOID&)g_InputOrig[I], reinterpret_cast<void*>(&InputThunk<I>))), ...);
        }
        std::deque<std::string> g_FullQueue;       // waiting (names)

        struct Gpu
        {
            ComPtr<ID3D11VertexShader> Vs;
            ComPtr<ID3D11PixelShader> Ps;
            ComPtr<ID3D11SamplerState> Sampler;
            ComPtr<ID3D11Texture2D> Texture;
            ComPtr<ID3D11ShaderResourceView> View;
            uint32_t Width = 0, Height = 0;
            bool Failed = false;
        } g_Gpu;

        constexpr const char kShader[] = R"(
struct V { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
V vs(uint id : SV_VertexID) { V o; float2 uv = float2((id << 1) & 2, id & 2); o.uv = uv; o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1); return o; }
Texture2D t : register(t0); SamplerState s : register(s0);
float4 ps(V i) : SV_Target { return float4(t.Sample(s, i.uv).rgb, 1); }
)";

        bool CreateShaders(ID3D11Device* device)
        {
            if (g_Gpu.Vs)
                return true;
            if (g_Gpu.Failed)
                return false;
            g_Gpu.Failed = true;
            HMODULE compiler = LoadLibraryW(L"d3dcompiler_47.dll");
            auto compile = compiler ? reinterpret_cast<pD3DCompile>(GetProcAddress(compiler, "D3DCompile")) : nullptr;
            if (!compile)
            {
                Logger::Log("Video: d3dcompiler_47.dll is missing, full-screen videos are off", MODULE_NAME, 2);
                return false;
            }
            ComPtr<ID3DBlob> vs, ps, errors;
            if (FAILED(compile(kShader, sizeof(kShader) - 1, "video", nullptr, nullptr, "vs", "vs_4_0", 0, 0, &vs, &errors)) ||
                FAILED(compile(kShader, sizeof(kShader) - 1, "video", nullptr, nullptr, "ps", "ps_4_0", 0, 0, &ps, &errors)) ||
                FAILED(device->CreateVertexShader(vs->GetBufferPointer(), vs->GetBufferSize(), nullptr, &g_Gpu.Vs)) ||
                FAILED(device->CreatePixelShader(ps->GetBufferPointer(), ps->GetBufferSize(), nullptr, &g_Gpu.Ps)))
                return false;
            D3D11_SAMPLER_DESC sampler{};
            sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
            sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
            sampler.MaxLOD = D3D11_FLOAT32_MAX;
            if (FAILED(device->CreateSamplerState(&sampler, &g_Gpu.Sampler)))
                return false;
            g_Gpu.Failed = false;
            return true;
        }

        bool EnsureTexture(ID3D11Device* device, uint32_t width, uint32_t height)
        {
            if (g_Gpu.Texture && g_Gpu.Width == width && g_Gpu.Height == height)
                return true;
            g_Gpu.Texture.Reset();
            g_Gpu.View.Reset();
            D3D11_TEXTURE2D_DESC desc{};
            desc.Width = width;
            desc.Height = height;
            desc.MipLevels = desc.ArraySize = 1;
            desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            desc.SampleDesc.Count = 1;
            desc.Usage = D3D11_USAGE_DEFAULT;
            desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
            if (FAILED(device->CreateTexture2D(&desc, nullptr, &g_Gpu.Texture)) ||
                FAILED(device->CreateShaderResourceView(g_Gpu.Texture.Get(), nullptr, &g_Gpu.View)))
                return false;
            g_Gpu.Width = width;
            g_Gpu.Height = height;
            return true;
        }

        std::unique_ptr<Session> OpenFull(const std::string& name)
        {
            const std::wstring mp4 = FindMp4(name);
            if (mp4.empty())
            {
                Logger::Log(std::format("Video: no movies\\{}.mp4 in Yu-Gi-Oh-Ex or a mod", name), MODULE_NAME, 1);
                return nullptr;
            }
            auto s = std::make_unique<Session>();
            s->Path = mp4;
            s->Worker = std::thread(Work, s.get());
            g_BlockInput = true;
            Logger::Log(std::format("Video: full screen {}", Narrow(mp4)), MODULE_NAME, 0);
            return s;
        }

        void CloseFull(std::unique_ptr<Session>& s)
        {
            if (!s)
                return;
            s->Stop = true;
            if (s->Worker.joinable())
                s->Worker.join();
            if (s->Started && s->HasAudio)
                Player::StopPcm();
            s.reset();
            g_BlockInput = !g_FullQueue.empty();   // the next video in the queue keeps the game's input off
        }

        bool SkipPressed()
        {
            static bool wasDown = false;
            HWND window = GetForegroundWindow();
            DWORD pid = 0;
            GetWindowThreadProcessId(window, &pid);
            const bool down = pid == GetCurrentProcessId() && ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) || (GetAsyncKeyState(VK_LBUTTON) & 0x8000));
            const bool pressed = down && !wasDown;
            wasDown = down;
            return pressed;
        }

        void DrawFull(IDXGISwapChain* chain)
        {
            std::lock_guard lock(g_FullLock);
            if (!g_Full && !g_FullQueue.empty())
            {
                g_Full = OpenFull(g_FullQueue.front());
                g_FullQueue.pop_front();
            }
            if (!g_Full)
                return;
            Session& s = *g_Full;
            if (SkipPressed() || Finished(s))
            {
                CloseFull(g_Full);
                return;
            }

            ComPtr<ID3D11Device> device;
            if (FAILED(chain->GetDevice(IID_PPV_ARGS(&device))) || !CreateShaders(device.Get()))
                return;
            ComPtr<ID3D11DeviceContext> dc;
            device->GetImmediateContext(&dc);
            uint32_t width, height;
            {
                std::lock_guard sizeLock(s.Lock);
                width = s.Width;
                height = s.Height;
            }
            if (width == 0 || height == 0 || !EnsureTexture(device.Get(), width, height))
                return;   // still opening
            if (auto frame = TakeDue(s))
                dc->UpdateSubresource(g_Gpu.Texture.Get(), 0, nullptr, frame->Pixels.data(), width * 4, 0);
            if (!s.Started || s.Shown < 0.0)
                return;   // nothing to show yet: the game stays visible

            ComPtr<ID3D11Texture2D> back;
            if (FAILED(chain->GetBuffer(0, IID_PPV_ARGS(&back))))
                return;
            D3D11_TEXTURE2D_DESC backDesc{};
            back->GetDesc(&backDesc);
            ComPtr<ID3D11RenderTargetView> target;
            if (FAILED(device->CreateRenderTargetView(back.Get(), nullptr, &target)))
                return;

            // Save what we change, draw, put it back (the game and the GUI overlay draw after us with their own state).
            ComPtr<ID3D11RenderTargetView> oldTargets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT];
            ComPtr<ID3D11DepthStencilView> oldDepth;
            dc->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, oldTargets[0].GetAddressOf(), &oldDepth);
            UINT viewportCount = D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE;
            D3D11_VIEWPORT oldViewports[D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE];
            dc->RSGetViewports(&viewportCount, oldViewports);
            ComPtr<ID3D11RasterizerState> oldRaster;
            dc->RSGetState(&oldRaster);
            ComPtr<ID3D11BlendState> oldBlend;
            float oldFactor[4];
            UINT oldMask;
            dc->OMGetBlendState(&oldBlend, oldFactor, &oldMask);
            ComPtr<ID3D11DepthStencilState> oldDepthState;
            UINT oldRef;
            dc->OMGetDepthStencilState(&oldDepthState, &oldRef);
            ComPtr<ID3D11VertexShader> oldVs;
            ComPtr<ID3D11PixelShader> oldPs;
            dc->VSGetShader(&oldVs, nullptr, nullptr);
            dc->PSGetShader(&oldPs, nullptr, nullptr);
            ComPtr<ID3D11ShaderResourceView> oldView;
            dc->PSGetShaderResources(0, 1, &oldView);
            ComPtr<ID3D11SamplerState> oldSampler;
            dc->PSGetSamplers(0, 1, &oldSampler);
            ComPtr<ID3D11InputLayout> oldLayout;
            dc->IAGetInputLayout(&oldLayout);
            D3D11_PRIMITIVE_TOPOLOGY oldTopology;
            dc->IAGetPrimitiveTopology(&oldTopology);

            const float black[4] = { 0, 0, 0, 1 };
            dc->ClearRenderTargetView(target.Get(), black);
            const float scale = std::min(static_cast<float>(backDesc.Width) / width, static_cast<float>(backDesc.Height) / height);
            D3D11_VIEWPORT viewport{};
            viewport.Width = width * scale;
            viewport.Height = height * scale;
            viewport.TopLeftX = (backDesc.Width - viewport.Width) / 2;
            viewport.TopLeftY = (backDesc.Height - viewport.Height) / 2;
            viewport.MaxDepth = 1.0f;
            ID3D11RenderTargetView* targets[] = { target.Get() };
            dc->OMSetRenderTargets(1, targets, nullptr);
            dc->RSSetViewports(1, &viewport);
            dc->RSSetState(nullptr);
            dc->OMSetBlendState(nullptr, nullptr, 0xFFFFFFFF);
            dc->OMSetDepthStencilState(nullptr, 0);
            dc->IASetInputLayout(nullptr);
            dc->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            dc->VSSetShader(g_Gpu.Vs.Get(), nullptr, 0);
            dc->PSSetShader(g_Gpu.Ps.Get(), nullptr, 0);
            ID3D11ShaderResourceView* views[] = { g_Gpu.View.Get() };
            dc->PSSetShaderResources(0, 1, views);
            ID3D11SamplerState* samplers[] = { g_Gpu.Sampler.Get() };
            dc->PSSetSamplers(0, 1, samplers);
            dc->Draw(3, 0);

            ID3D11RenderTargetView* restore[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT];
            for (UINT i = 0; i < D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT; ++i)
                restore[i] = oldTargets[i].Get();
            dc->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, restore, oldDepth.Get());
            dc->RSSetViewports(viewportCount, oldViewports);
            dc->RSSetState(oldRaster.Get());
            dc->OMSetBlendState(oldBlend.Get(), oldFactor, oldMask);
            dc->OMSetDepthStencilState(oldDepthState.Get(), oldRef);
            dc->VSSetShader(oldVs.Get(), nullptr, 0);
            dc->PSSetShader(oldPs.Get(), nullptr, 0);
            ID3D11ShaderResourceView* restoreView[] = { oldView.Get() };
            dc->PSSetShaderResources(0, 1, restoreView);
            ID3D11SamplerState* restoreSampler[] = { oldSampler.Get() };
            dc->PSSetSamplers(0, 1, restoreSampler);
            dc->IASetInputLayout(oldLayout.Get());
            dc->IASetPrimitiveTopology(oldTopology);
        }

        HRESULT __stdcall Hook_Present(IDXGISwapChain* chain, UINT interval, UINT flags)
        {
            DrawFull(chain);
            return orig_Present(chain, interval, flags);
        }

        // ---- story scenes: a "command" line with Position VIDEO plays movies\<Expression>.mp4 ----
        // StoryScene_PlayLine (0x14082F890) plays the line at scene+684 of g_pStoryScriptFile (lines of 4 char*: speaker, position,
        // expression, text) and returns 1 for a line that shows nothing (BG / PROP commands): the scene goes on to the next line, which then
        // waits under the video (the game gets no input while it plays).
        struct ScriptLine { const char* Speaker; const char* Position; const char* Expression; const char* Text; };
        struct ScriptFile { ScriptLine* Lines; uint32_t ScriptCount; uint32_t LineCount; };
        using PlayLine_t = char(__fastcall*)(uintptr_t scene);
        PlayLine_t orig_PlayLine = reinterpret_cast<PlayLine_t>(0x14082F890);   // StoryScene_PlayLine

        char __fastcall Hook_PlayLine(uintptr_t scene)
        {
            const auto* file = *reinterpret_cast<ScriptFile* const*>(YGO::Addr::g_pStoryScriptFile);
            const uint32_t index = *reinterpret_cast<const uint32_t*>(scene + 684);
            if (file && index < file->LineCount)
            {
                const ScriptLine& line = file->Lines[index];
                if (line.Position && _stricmp(line.Position, "VIDEO") == 0)
                {
                    if (line.Expression && *line.Expression)
                        PlayFullScreen(line.Expression);
                    return 1;
                }
            }
            return orig_PlayLine(scene);
        }

        // ---- videos.json and RIX actions ----

        struct StoryVideos { std::string Before, After; };
        std::map<int, StoryVideos> g_StoryVideos;
        std::vector<std::string> g_Startup;

        std::vector<std::string> Names(const nlohmann::json& node)
        {
            std::vector<std::string> names;
            if (node.is_string())
                names.push_back(node.get<std::string>());
            else if (node.is_array())
                for (auto& item : node)
                    if (item.is_string())
                        names.push_back(item.get<std::string>());
            return names;
        }

        void LoadConfig()
        {
            // Yu-Gi-Oh-Ex\videos.json (and mods'): { "startup": "intro" | ["a", "b"], "storyDuels": { "186": { "before": "x", "after": "y" } } }
            try
            {
                const nlohmann::json root = YGO::Mods::ReadMerged("videos.json");
                if (!root.is_object())
                    return;
                if (root.contains("startup"))
                    g_Startup = Names(root["startup"]);
                if (auto duels = root.find("storyDuels"); duels != root.end() && duels->is_object())
                    for (auto& [id, entry] : duels->items())
                    {
                        if (!entry.is_object())
                            continue;
                        StoryVideos v{ entry.value("before", std::string()), entry.value("after", std::string()) };
                        g_StoryVideos[std::atoi(id.c_str())] = v;
                    }
                Logger::Log(std::format("videos.json: {} startup video(s), {} story duel(s) with videos", g_Startup.size(), g_StoryVideos.size()), MODULE_NAME, 0);
            }
            catch (const std::exception& e)
            {
                Logger::Log(std::format("videos.json is not valid JSON ({}): its videos are left out", e.what()), MODULE_NAME, 2);
            }
        }

        void __cdecl RunVideoAction(int, void* user)
        {
            PlayFullScreen(*static_cast<const std::string*>(user));
        }

        // Every movies\*.mp4 becomes a RIX action "video:<name>" (menus and pages: { "video": "<name>" } or { "call": "video:<name>" }).
        bool RegisterActions()
        {
            if (!RIX::Load() || !RIX::Functions().RegisterAction)
                return false;
            int count = 0;
            for (const auto& path : YGO::Mods::FilesIn("movies", L".mp4"))
            {
                auto* name = new std::string(Narrow(path.stem().wstring()));   // kept for the process's life
                if (RIX::Functions().RegisterAction(("video:" + *name).c_str(), RunVideoAction, name))
                    ++count;
            }
            Logger::Log(std::format("Video: {} RIX action(s) video:<name>", count), MODULE_NAME, 0);
            return true;
        }
    }

    void PlayFullScreen(const std::string& name)
    {
        if (name.empty())
            return;
        std::lock_guard lock(g_FullLock);
        g_FullQueue.push_back(name);
    }

    void Tick()
    {
        // Once the game's swap chain exists: hook Present, register the RIX actions, start the startup videos.
        static bool presentHooked = false, actions = false;
        if (!presentHooked && *g_SwapChain)
        {
            void** vtable = *reinterpret_cast<void***>(*g_SwapChain);
            orig_Present = reinterpret_cast<Present_t>(vtable[8]);
            DetourTransactionBegin();
            DetourUpdateThread(GetCurrentThread());
            DetourAttach(&(PVOID&)orig_Present, Hook_Present);
            const LONG error = DetourTransactionCommit();
            presentHooked = true;
            Logger::Log(std::format("Video: full-screen player {}", error == NO_ERROR ? "ready" : std::format("FAILED (Detours error {})", error)),
                MODULE_NAME, error == NO_ERROR ? 0 : 2);
            for (const auto& name : g_Startup)
                PlayFullScreen(name);
        }
        static int tries = 0;
        if (!actions && presentHooked && ++tries % 20 == 0)
            actions = RegisterActions() || tries > 2000;
    }

    void OnDuelStart(int storyDuel)
    {
        if (auto it = g_StoryVideos.find(storyDuel); it != g_StoryVideos.end() && !it->second.Before.empty())
            PlayFullScreen(it->second.Before);
    }

    void OnDuelEnd(int storyDuel)
    {
        if (auto it = g_StoryVideos.find(storyDuel); it != g_StoryVideos.end() && !it->second.After.empty())
            PlayFullScreen(it->second.After);
    }

    void Setup()
    {
        LoadConfig();
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_MovieOpen, Hook_MovieOpen);
        DetourAttach(&(PVOID&)orig_MovieUpdate, Hook_MovieUpdate);
        DetourAttach(&(PVOID&)orig_MovieClose, Hook_MovieClose);
        DetourAttach(&(PVOID&)orig_FileExists, Hook_FileExists);
        AttachInputReads(std::make_index_sequence<kInputReadCount>());
        DetourAttach(&(PVOID&)orig_PlayLine, Hook_PlayLine);
        const LONG error = DetourTransactionCommit();
        Logger::Log(std::format("Video hooks {}", error == NO_ERROR ? "attached" : std::format("FAILED (Detours error {})", error)),
            MODULE_NAME, error == NO_ERROR ? 0 : 2);
    }

    void Play(const std::string& name)
    {
        std::lock_guard lock(g_QueueLock);
        g_Queue.push_back(name);
    }

    void OnSummon(uint16_t card)
    {
        if (std::find(kGameCutIns.begin(), kGameCutIns.end(), card) != kGameCutIns.end())
            return;   // the game plays its own cut-in (an mp4 for it replaces the picture)
        const std::string name = std::to_string(card);
        if (!FindMp4(name).empty())
            Play(name);
    }

    void Pump()
    {
        if (*g_MovieShowing)
            return;   // one movie at a time, as the game does
        std::string name;
        {
            std::lock_guard lock(g_QueueLock);
            if (g_Queue.empty())
                return;
            name = std::move(g_Queue.front());
            g_Queue.pop_front();
        }
        StartMovie(name);
    }
}
