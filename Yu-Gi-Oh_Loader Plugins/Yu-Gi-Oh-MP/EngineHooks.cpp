#include <Windows.h>
#include <detours.h>

#include <atomic>
#include <cstdint>
#include <format>

#include "DuelRecorder.h"
#include "EngineHooks.h"
#include "Logger.h"
#include "P2P.h"
#include "Stubs.h"

// Logging stubs on every multiplayer / duel-flow / stats function mapped so far (docs/MultiplayerSystem.md,
// docs/StatsAndMatchResults.md; names and parameter names match YuGiOh.exe.i64). All of them call straight through -
// this is the "confirm it's real and see the order things happen in" pass before any of it is replaced. Stub.h
// explains the line format. Rare events log at Info (console); hot per-frame/per-tick functions log at Debug
// (console.log only). Every stub stops logging after 3 calls. The duel's own event stream (engine messages, card moves, LP, chains,
// net events) is Quiet here and goes to Duels.log through DuelRecorder taps instead - that's what DuelIt replays.
//
// Not hooked: trivial getters (Get_*, LiveSeats__Get_*, < 0x10 bytes, polled constantly), thunks (j_*),
// Duel__PlayerRecord_SetControllerType (4 bytes, too short to patch), Duel__NetEvent__ClassifyRoutingType (a jmp).
namespace
{
    using namespace Stubs;

    // ---- Duel__NetEvent__Send (0x14081E3B0): the in-duel event bus send (LiveManager channel 3). Hand-written because it
    // also mirrors every event to our server as a P2P::EventReport (fire-and-forget, never blocks the duel).
    std::atomic<uint32_t> g_EventSequence = 0;
    std::atomic<uint32_t> g_EventSendLogged = 0;
    using NetEventSend_t = u64(*)(const uint16_t*, u64);
    NetEventSend_t orig_NetEventSend = reinterpret_cast<NetEventSend_t>(0x14081E3B0);

    using GetLocalPlayerSeatParity_t = int(__fastcall*)();
    GetLocalPlayerSeatParity_t Call_GetLocalPlayerSeatParity = reinterpret_cast<GetLocalPlayerSeatParity_t>(0x140768F90);

    u64 Hook_NetEventSend(const uint16_t* payload, u64 payloadBytesRaw)
    {
        const int payloadBytes = static_cast<int>(payloadBytesRaw);
        if (payload && payloadBytes >= 2)
        {
            P2P::EventReport report;
            report.SequenceNumber = ++g_EventSequence;
            report.Seat = static_cast<uint32_t>(Call_GetLocalPlayerSeatParity());
            report.EventType = payload[0];
            const auto* bytes = reinterpret_cast<const uint8_t*>(payload + 1);
            report.Payload.assign(bytes, bytes + (payloadBytes - 2));
            P2P::ReportEvent(report);
            DuelRecorder::OnNetSend(report.SequenceNumber, report.EventType, bytes, payloadBytes - 2);

            if (++g_EventSendLogged <= kMaxLogged)
                Logger::WriteLog(std::format("[stub] Duel__NetEvent__Send(type={}({}), payload={}) seq={} from 0x{:X} [{}/{}, all of them go to Duels.log]",
                    EventTypeName(report.EventType), report.EventType, HexDump(bytes, payloadBytes - 2, 64), report.SequenceNumber,
                    reinterpret_cast<uintptr_t>(_ReturnAddress()), g_EventSendLogged.load(), kMaxLogged), MODULE_NAME, Debug);
        }
        return orig_NetEventSend(payload, payloadBytesRaw);
    }

    // ---- YGO::DUEL::Set_IsDuelMultiplayer (0x140769720): also called directly by the debug helper below.
    using SetIsDuelMultiplayer_t = void(__fastcall*)(bool);
    SetIsDuelMultiplayer_t Call_SetIsDuelMultiplayer = reinterpret_cast<SetIsDuelMultiplayer_t>(0x140769720);

    template <typename... S>
    int AttachAll()
    {
        return (static_cast<int>(S::Attach()) + ...);
    }

    // Kept as a list of types so adding a function is one line.
    int AttachStubs()
    {
        int attached = 0;

        // ===== Lobby / session setup (channel 2 message queue, types 1-7) =====
        attached += AttachAll<
            Stub<0x140772F40, "Duel__NetSession__Update", "duelScreen:ptr,deltaTime:float", Debug, u64(u64, float)>,
            RecvStub<0x1407BE550, "Duel__NetQueue__DequeueIncoming", "outBuf:hex@*1,outSize:p64", Debug, u64(u64, u64)>,
            Stub<0x1407BE5A0, "Duel__NetQueue__EnqueueOutgoing", "src:hex@1,size", Debug, u64(u64, u64)>,
            Stub<0x1407B4F50, "Duel__NetLobby__HandleMessage_Type1to6", "lobbyCtx:ptr,msg:hex5", Info, u64(u64, u64)>,
            Stub<0x1407BF200, "Duel__NetMsg_Type7_MarkSeatReady", "msg:hex5", Info, u64(u64)>,
            Stub<0x1407AF740, "Duel__NetLobby__SendType1_SeatPhase", "lobbyCtx:ptr,phase:u8", Info, u64(u64, u64)>,
            Stub<0x1407AF7A0, "Duel__NetLobby__PhaseBarrier_SendType2", "lobbyCtx:ptr,phase:i32", Debug, u64(u64, u64)>,
            Stub<0x1407B4100, "Duel__Lobby__ResolveStartingPlayer_State0xB", "lobbyCtx:ptr", Debug, u64(u64)>,
            Stub<0x1407B4D20, "Duel__NetLobby__CoinToss_Update", "lobbyCtx:ptr,deltaTime:float", Debug, u64(u64, float)>,
            Stub<0x1407D03E0, "Duel__NetLobby__WaitForAllReady_Update", "readyCtx:ptr,deltaTime:float", Debug, u64(u64, float)>,
            RecvStub<0x14081E010, "Duel__LiveManager_TransportReceive", "outBuf:hex@*1,ioSize:p64", Info, u64(u64, u64)>,
            Stub<0x14081E060, "Duel__LiveManager_TransportSend", "data:hex@1,size,targetSeat:i32", Info, u64(u64, u64, u64)>
        >();

        // ===== Duel setup =====
        attached += AttachAll<
            Stub<0x14081E530, "YGO__DuelSetup__AssignSeatDecksAndDuelists", "", Info, u64()>,
            Stub<0x14081ED30, "YGO::DuelSetup::SetupDuelSettings", "", Info, u64()>,
            Stub<0x1407696E0, "YGO::DuelSetup::Setup_RuleAndLP", "", Info, u64()>,
            Stub<0x1407BC4C0, "YGO::DUEL::Engine_Init", "", Info, u64(), DuelRecorder::Tap_EngineInit>,
            Stub<0x1407BC910, "YGO::DUEL::Engine_InitRulesAndDecks", "", Info, u64()>,
            Stub<0x14005FC90, "YGO::DUEL::DuelSetup_ClearState", "", Info, u64()>,
            Stub<0x14005FD90, "YGO::DUEL::DuelSetup_InitEngine", "", Info, u64()>,
            Stub<0x14005FED0, "YGO::DUEL::DuelSetup_LoadBothDecks", "", Info, u64()>,
            Stub<0x140769720, "YGO::DUEL::Set_IsDuelMultiplayer", "isMP:bool", Info, u64(u64)>,
            Stub<0x140769AC0, "YGO::DUEL::Set_IsTagDuel", "isTag:bool", Info, u64(u64)>,
            Stub<0x1407697F0, "YGO__DUEL__Set_LiveMatchType", "type:u8", Info, u64(u64)>,
            Stub<0x1407696C0, "YGO__DUEL__Set_IsLiveSession", "on:bool", Info, u64(u64)>,
            Stub<0x140769740, "YGO::DUEL::Set_StartingPlayer", "player:i32", Info, u64(u64), DuelRecorder::Tap_SetStartingPlayer>,
            Stub<0x140769850, "YGO__DUEL__Set_LocalPlayerSeat", "seat:i32", Info, u64(u64)>
        >();

        // ===== In-duel event bus (channel 3) + engine message queue =====
        attached += AttachAll<
            RecvStub<0x14081E220, "Duel__NetEvent__Receive", "outEvent:hex48", Quiet, u64(u64), DuelRecorder::Tap_NetEventReceive>,
            Stub<0x14000EA80, "Duel__NetEvent__PumpOne", "", Debug, u64()>,
            Stub<0x14000E990, "Duel__NetEvent__SendSimple", "type:evt,typeOverride:i32,payload:hex@3,payloadBytes:i32", Debug, u64(u64, u64, u64, u64)>,
            Stub<0x14000E8C0, "Duel::NetEvent::Send4", "type:evt,a:i16,b:i16,c:i16", Debug, u64(u64, u64, u64, u64)>,
            Stub<0x14003EAC0, "Duel__MasterTick_GatedByNetEvent", "", Debug, u64()>,
            Stub<0x140155EC0, "Duel__Net__ServeRemoteTurnRequests", "", Debug, u64()>,
            Stub<0x140100B10, "Duel__NetEvent__QueueAnimationTrigger", "animCode:u16,param:hex", Debug, u64(u64, u64)>,
            Stub<0x1400AA860, "Duel__Action__RegisterCandidate", "toActionList:i32,packedEffectRef:hex,slotIndex:u16,param:hex", Debug, u64(u64, u64, u64, u64)>,
            Stub<0x1401035A0, "Duel__IdleCommand__StateMachine", "effectCtx:ptr,side:i32", Debug, u64(u64, u64)>,
            Stub<0x14000F400, "Duel__MsgQueue__ExecuteFront", "", Debug, u64()>,
            // Duel__MsgQueue__Push is deliberately not stubbed (register assumptions, see Stubs.h); the recorder reads each
            // message as PumpAndMirror pops it instead.
            Stub<0x1400117E0, "Duel__MsgQueue__PumpAndMirror", "", Debug, u64(), DuelRecorder::Tap_PumpAndMirror>,
            Stub<0x1407C1450, "YGO::UI::Draw_DuelAnimationFromId", "anim:u32,a2:i32,a3:i32,a4:i32", Debug, u64(u64, u64, u64, u64)>
        >();

        // ===== LP / stats during the duel =====
        attached += AttachAll<
            Stub<0x14009E4C0, "YGO__Effects__LP_ApplyDamage_Internal", "effectCtx:ptr,side:u32,amount:u32", Quiet, u64(u64, u64, u64), DuelRecorder::Tap_LPDamage>,
            Stub<0x1400A15F0, "YGO__Effects__LP_ApplyGain_Internal", "effectCtx:ptr,side:u32,amount:i32", Quiet, u64(u64, u64, u64), DuelRecorder::Tap_LPGain>,
            Stub<0x1400A1A40, "YGO::Effects::LP_ApplyChangeFromParamTable", "effectCtx:ptr", Quiet, u64(u64)>,
            Stub<0x14076ABB0, "YGO__Stats__OnLifePointsChanged", "damagedSide:i32,delta:i32,cause:u32", Quiet, u64(u64, u64, u64)>,
            Stub<0x1407C2B80, "YGO__UI__OnCardMove", "cardIndex:i32,moveKind:i32,fromLoc:hex,toLoc:hex", Quiet, u64(u64, u64, u64, u64), DuelRecorder::Tap_OnCardMove>,
            Stub<0x1407C31F0, "YGO__UI__OnChainResolve", "side:u32,a2:u32,chainLength:i32", Quiet, u64(u64, u64, u64), DuelRecorder::Tap_OnChainResolve>,
            Stub<0x14076AFE0, "YGO__Stats__RecordCardUsed", "seat:u32,cardId:u16", Quiet, u64(u64, u64)>,
            Stub<0x14076B120, "DuelStatBlock__RecordCardUsed", "statBlock:ptr,seat:i32,cardId:u16", Quiet, u64(u64, u64, u64)>,
            // Compiled-out telemetry: declared void() but called with the event's context still in the argument
            // registers, so log all four raw.
            Stub<0x14076B000, "YGO__Stats__StrippedTelemetryStub", "rcx:hex,rdx:hex,r8:hex,r9:hex", Debug, u64(u64, u64, u64, u64)>,
            Stub<0x14076AF20, "YGO__Stats__StrippedTelemetryStub4", "rcx:hex,rdx:hex,r8:hex,r9:hex", Debug, u64(u64, u64, u64, u64)>
        >();

        // ===== Duel end, match result, save/stats =====
        attached += AttachAll<
            Stub<0x14003D920, "Duel__DeclareLoss", "losingSide:i32", Info, u64(u64)>,
            Stub<0x14003E0E0, "Duel__EndHandshake", "a1:hex,a2:hex,a3:hex", Debug, u64(u64, u64, u64)>,
            Stub<0x140112650, "YGO__DUEL__ResolveDuelEnd", "", Info, u64()>,
            Stub<0x140147DD0, "YGO__DUEL__CheckTurnOrTimeLimit", "", Info, u64()>,
            Stub<0x1407C3420, "YGO__DUEL__OnDuelEnd", "outcome:i32", Info, u64(u64), DuelRecorder::Tap_OnDuelEnd>,
            Stub<0x140769A60, "YGO__DUEL__Set_RoundResult", "round:i32,outcome:i32,localParity:i32", Info, u64(u64, u64, u64), DuelRecorder::Tap_SetRoundResult>,
            Stub<0x140769500, "YGO__DUEL__ResetMatchResults", "", Info, u64()>,
            Stub<0x140769210, "YGO__DUEL__IsMatchOver", "matchState:ptr", Debug, u64(u64)>,
            Stub<0x1407692F0, "YGO__DUEL__DidSideWinMatch", "sideParity:i32", Debug, u64(u64)>,
            Stub<0x140768E40, "YGO__DUEL__Get_DuelMatchResult", "sideParity:i32", Debug, u64(u64)>,
            Stub<0x14087F250, "YGO::DUEL::FinishAndUpdateSave", "resultScreen:ptr,rewardCtx:ptr,dpReward:i32", Info, u64(u64, u64, u64), DuelRecorder::Tap_FinishAndUpdateSave>,
            Stub<0x1407F9420, "Steam::AchievementProgress::UpdateSaveStat", "statsSection:ptr,stat:u32,delta:i64", Info, u64(u64, u64, u64), DuelRecorder::Tap_UpdateSaveStat>,
            Stub<0x140858770, "RIX::ScreenMatchResult::Constructor", "self:ptr", Info, u64(u64)>
        >();

        // ===== LiveManager (network thread runs NetThreadTick every ~1 ms) =====
        attached += AttachAll<
            Stub<0x140802130, "RIX__LiveManager__ctor", "self:ptr", Info, u64(u64)>,
            // Not a run-forever entry: the thread's own loop (0x140821700) calls it every ~4 ms, it's the per-tick body.
            Stub<0x140803410, "RIX__LiveManager__NetThreadEntry", "self:ptr", Debug, u64(u64)>,
            Stub<0x140803440, "RIX__LiveManager__NetThreadTick", "self:ptr", Debug, u64(u64)>,
            Stub<0x1408029A0, "RIX__LiveManager__ProcessPendingCommand", "self:ptr", Debug, u64(u64)>,
            Stub<0x140804530, "RIX__LiveManager__HeartbeatTick", "self:ptr", Debug, u64(u64)>,
            Stub<0x140804980, "RIX__LiveManager__TransmitDeckInfo", "self:ptr", Debug, u64(u64)>,
            Stub<0x1408038A0, "RIX__LiveManager__FlushOutgoingAndRouteIncoming", "self:ptr", Debug, u64(u64)>,
            Stub<0x140803FF0, "RIX__LiveManager__WriteChannel", "self:ptr,data:hex@2,size,channel:i32,seatMask:hex", Debug, u64(u64, u64, u64, u64, u64)>,
            RecvStub<0x140803660, "RIX__LiveManager__ReadChannel", "self:ptr,outBuf:hex@*2,ioSize:p64,channel:i32", Debug, u64(u64, u64, u64, u64)>,
            Stub<0x140802C80, "RIX__LiveManager__SubmitOrCollectLobbyCommand", "self:ptr", Debug, u64(u64)>,
            Stub<0x140803780, "RIX__LiveManager__SubmitOrCollectCommand", "self:ptr,command:hex8+28", Debug, u64(u64, u64)>,
            Stub<0x140803DF0, "RIX__LiveManager__ResetSession", "self:ptr", Info, u64(u64)>,
            Stub<0x140803F80, "RIX__LiveManager__ClearDeckReceivedFlags", "self:ptr", Info, u64(u64)>,
            Stub<0x140802870, "RIX__LiveManager__ClearSeatBlocks", "self:ptr", Info, u64(u64)>,
            Stub<0x140802900, "RIX__LiveManager__ClearSeatDecks", "self:ptr", Info, u64(u64)>,
            Stub<0x140804160, "RIX__LiveManager__SetLocalState", "self:ptr,state:i32", Info, u64(u64, u64)>,
            Stub<0x140804210, "RIX__LiveManager__OnSeatContextPacket", "self:ptr,ctx:hex8,seat:u32", Debug, u64(u64, u64, u64)>,
            Stub<0x1408042B0, "RIX__LiveManager__SetSeatDeck", "self:ptr,deck:hex8,seat:u32,bTransmit:bool", Info, u64(u64, u64, u64, u64)>,
            Stub<0x1408043C0, "RIX__LiveManager__SetHostContext", "self:ptr,ctx:hex24", Debug, u64(u64, u64)>,
            Stub<0x140804480, "RIX__LiveManager__SetMatchSettings", "self:ptr,settings12:hex12", Info, u64(u64, u64)>
        >();

        // LiveManager getters (polled by the Live* screens) - only a few calls each, to confirm who polls them.
        attached += AttachAll<
            Stub<0x140802E20, "RIX__LiveManager__GetSeatState", "self:ptr,seat:u32", Debug, u64(u64, u64)>,
            Stub<0x140802EB0, "RIX__LiveManager__GetHostState", "self:ptr", Debug, u64(u64)>,
            Stub<0x140802F70, "RIX__LiveManager__GetSeatContextAndRxTime", "self:ptr,outCtx:ptr,seat:u32,outRxMs:ptr", Debug, u64(u64, u64, u64, u64)>,
            Stub<0x140803010, "RIX__LiveManager__GetRemoteRxTimeRange", "self:ptr,outMin:ptr,outMax:ptr", Debug, u64(u64, u64, u64)>,
            Stub<0x140803100, "RIX__LiveManager__GetSeatDeck", "self:ptr,outDeck:ptr,seat:u32", Debug, u64(u64, u64, u64)>,
            Stub<0x1408031F0, "RIX__LiveManager__GetHostContext", "self:ptr,out:ptr,outRxMs:ptr", Debug, u64(u64, u64, u64)>,
            Stub<0x140803280, "RIX__LiveManager__AllSeatsInState", "self:ptr,state:i32", Debug, u64(u64, u64)>,
            Stub<0x1408026A0, "RIX__LiveManager__AllSeatsInAnyState3", "self:ptr,stateA:i32,stateB:i32,stateC:i32", Debug, u64(u64, u64, u64, u64)>,
            Stub<0x140803320, "RIX__LiveManager__IsCommandIdle", "self:ptr", Debug, u64(u64)>,
            Stub<0x140803380, "RIX__LiveManager__AllDecksReceived", "self:ptr", Debug, u64(u64)>
        >();

        // ===== LiveSeats =====
        attached += AttachAll<
            Stub<0x140769E30, "LiveSeats__FindOrAssignBySteamID", "steamID:hex", Debug, u64(u64)>,
            Stub<0x14076A2E0, "LiveSeats__SnapshotForDuel", "", Info, u64()>,
            Stub<0x14076A3E0, "LiveSeats__ClearAll", "", Info, u64()>,
            Stub<0x14076A510, "LiveSeats__SetAvatar", "seat:u32,avatarId:i32", Info, u64(u64, u64)>,
            Stub<0x14076A550, "LiveSeats__SetName", "seat:i32,name:wstr", Info, u64(u64, u64), DuelRecorder::Tap_LiveSeatsSetName>
        >();

        // ===== QNet (Steam lobbies + P2P) =====
        attached += AttachAll<
            Stub<0x1408D72F0, "QNet__ctor", "self:ptr", Info, u64(u64)>,
            Stub<0x1408D7930, "QNet__AssimilateSessionInformation", "self:ptr", Info, u64(u64)>,
            Stub<0x1408D7C00, "QNet__LockLobbyIfHost", "self:ptr", Info, u64(u64)>,
            Stub<0x1408D7C70, "QNet__CreateLobby", "self:ptr,settings:hex32", Info, u64(u64, u64)>,
            Stub<0x1408D7DC0, "QNet__PollState", "self:ptr", Debug, u64(u64)>,
            Stub<0x1408D7DE0, "QNet__DebugDumpLobby", "self:ptr", Info, u64(u64)>,
            Stub<0x1408D8020, "QNet__PublishHostLobbyData", "self:ptr,lobbySteamID:hex", Info, u64(u64, u64)>,
            Stub<0x1408D82B0, "QNet__RequestLobbyList", "self:ptr,filter64:hex64", Info, u64(u64, u64)>,
            Stub<0x1408D83F0, "QNet__GetLobbyListResults", "self:ptr,outVec:ptr", Info, u64(u64, u64)>,
            Stub<0x1408D8620, "QNet__GetLobbyMemberList", "self:ptr,outOwner:ptr", Info, u64(u64, u64)>,
            Stub<0x1408D88E0, "QNet__IsSteamOnline", "self:ptr", Debug, u64(u64)>,
            Stub<0x1408D8980, "QNet__JoinLobby", "self:ptr,lobbyItem:ptr", Info, u64(u64, u64)>,
            Stub<0x1408D8A90, "QNet__LeaveLobbyAndCloseP2P", "self:ptr,lobbySteamID:hex", Info, u64(u64, u64)>,
            Stub<0x1408D8BE0, "QNet__OnLobbyCreated", "self:ptr,pLobbyCreated_t:hex16,bIOFailure:bool", Info, u64(u64, u64, u64)>,
            Stub<0x1408D8D30, "QNet__OnLobbyMatchList", "self:ptr,pLobbyMatchList_t:p32,bIOFailure:bool", Info, u64(u64, u64, u64)>,
            Stub<0x1408D8D90, "QNet__OnLobbyEnter", "self:ptr,pLobbyEnter_t:hex24,bIOFailure:bool", Info, u64(u64, u64, u64)>,
            // Accepts a P2P session from ANY SteamID (anti-cheat hole, docs) - log who asked.
            Stub<0x1408D8F00, "QNet__OnP2PSessionRequest_AcceptAnyone", "callbackObj:ptr,requesterSteamID:p64", Info, u64(u64, u64)>,
            Stub<0x1408D8F50, "QNet__PumpP2PReceive", "self:ptr", Debug, u64(u64)>,
            Stub<0x1408D90F0, "QNet__SendP2PToPeer", "self:ptr,debugLabel:cstr,data:hex@3,size:u32,unusedRouteMask:i32,bReliable:bool", Debug, u64(u64, u64, u64, u64, u64, u64)>,
            Stub<0x1408D91A0, "QNet__PublishMyAvatar", "self:ptr,lobbySteamID:hex", Info, u64(u64, u64)>,
            Stub<0x14080BC10, "QNetLobbyEntry__PopulateFromLobby", "entry:ptr,lobbySteamID:hex", Debug, u64(u64, u64)>,
            Stub<0x140819400, "QNetBase__OnMatchStarted", "self:ptr", Info, u64(u64)>,
            Stub<0x140819420, "QNetBase__RunFindSessionCommand", "self:ptr,findCmd:ptr", Info, u64(u64, u64)>,
            // cmd +28 Status, +32 Kind (1 CreateGame, 2 RandomGame, 3 Join, 4 member list, 5 Leave).
            Stub<0x140819470, "QNetBase__RunSessionCommand", "self:ptr,cmd:hex8+28", Info, u64(u64, u64)>,
            Stub<0x140819620, "QNetBase__EnqueueReceivedPacket", "self:ptr,data:hex@2,size,senderSeat:i32,flags:i32", Debug, u64(u64, u64, u64, u64, u64)>,
            RecvStub<0x140819A50, "QNetBase__DequeueReceivedPacket", "self:ptr,outBuf:hex@*2,ioSize:p64,outSenderSeat:p32,outFlags:p32", Debug, u64(u64, u64, u64, u64, u64)>,
            Stub<0x140819C00, "QNetBase__SetConnectedFlag", "self:ptr,connected:bool", Info, u64(u64, u64)>
        >();

        // ===== Live* screens, main menu, leaderboards =====
        attached += AttachAll<
            Stub<0x140856C40, "RIX::ScreenMainMenu::ActivateItem", "screen:ptr,item:i32,fromInput:bool", Info, u64(u64, u64, u64)>,
            Stub<0x1408D00B0, "RIX::ScreenLiveLobby::OnEnter", "self:ptr,fromScreen:ptr", Info, u64(u64, u64)>,
            Stub<0x1408D0990, "RIX::ScreenLiveLobby::SetState", "self:ptr,state:i32", Info, u64(u64, u64)>,
            Stub<0x1408D03A0, "RIX::ScreenLiveLobby::UpdateState", "self:ptr", Debug, u64(u64)>,
            Stub<0x1408CFAA0, "RIX::ScreenLiveLobby::Update", "self:ptr,ui:ptr,deltaTime:float", Debug, u64(u64, u64, double)>,
            Stub<0x1408CF990, "RIX::ScreenLiveLobby::ExitToMenu", "self:ptr", Info, u64(u64)>,
            Stub<0x1408D3110, "RIX::ScreenLiveSession::Update", "self:ptr,ui:ptr", Debug, u64(u64, u64)>,
            Stub<0x1408518E0, "RIX::ScreenLiveLoading::Update", "self:ptr,ui:ptr,deltaTime:float", Debug, u64(u64, u64, double)>,
            Stub<0x140852440, "RIX::ScreenLiveMenu::Update", "self:ptr,ui:ptr", Debug, u64(u64, u64)>,
            Stub<0x140854CD0, "RIX::ScreenLiveSetting::Update", "self:ptr,ui:ptr", Debug, u64(u64, u64)>,
            Stub<0x14086CBE0, "RIX__LiveSession__AbortWithError", "errorCode:u32,bLeaveLobby:i32", Info, u64(u64, u64)>,
            Stub<0x1408D6400, "RIX__Leaderboards__Init_AllocAndFindAll", "", Info, u64()>,
            Stub<0x1408D6570, "RIX__CSteamLeaderboard__UploadScore_ByIndex", "profileId:i32,score:u32,boardIndex:i32", Info, u64(u64, u64, u64)>,
            Stub<0x1408D6630, "RIX__CSteamLeaderboard__UploadScore_UniqueCards", "profileId:i32,score:u32", Info, u64(u64, u64)>,
            Stub<0x1408D66F0, "RIX__CSteamLeaderboard__DownloadEntries", "listCtx:ptr,unused:i32,boardIndex:i32,view:i32,rangeStart:u32", Info, u64(u64, u64, u64, u64, u64)>,
            Stub<0x1408D62A0, "RIX__CSteamLeaderboard__OnDownloadEntries", "board:ptr,pScoresDownloaded:hex24,bIOFailure:bool", Info, u64(u64, u64, u64)>,
            Stub<0x1408D63E0, "RIX__CSteamLeaderboard__OnFindLeaderboard", "board:ptr,pFindResult:hex16,bIOFailure:bool", Info, u64(u64, u64, u64)>
        >();

        return attached;
    }
}

void EngineHooks::Setup()
{
    const int attached = AttachStubs();

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_NetEventSend, Hook_NetEventSend);
    const bool sendHooked = DetourTransactionCommit() == NO_ERROR;

    Logger::WriteLog(std::format("EngineHooks: {} logging stubs attached{} - hot functions log to console.log only (debug level)",
        attached + (sendHooked ? 1 : 0), sendHooked ? "" : ", Duel__NetEvent__Send FAILED"), MODULE_NAME, 0);
}

void EngineHooks::DEBUG_SetIsDuelMultiplayer(bool on)
{
    Logger::WriteLog(std::format("EngineHooks::DEBUG_SetIsDuelMultiplayer({})", on), MODULE_NAME, 0);
    Call_SetIsDuelMultiplayer(on);
}
