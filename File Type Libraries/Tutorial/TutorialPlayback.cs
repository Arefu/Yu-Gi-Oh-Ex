namespace Types
{
    /// <summary>What the screen shows at one frame of a tutorial (for previews).</summary>
    public sealed class TutorialState
    {
        public int Frame { get; set; }
        public int Step { get; set; }
        public int StepFrame { get; set; }
        public int Scenario { get; set; } = -1;
        public int TurnPlayer { get; set; }
        public int Phase { get; set; } = -1;
        public bool PhaseBanner { get; set; }
        public string? Message { get; set; }
        public bool MessageUpper { get; set; }
        public string? Hint { get; set; }
        public string? Waiting { get; set; }
        public (int Player, int Zone, int Index)? Cursor { get; set; }
        public (int Player, int Zone)? Pointer { get; set; }
        public int PointerUntil { get; set; }
        public bool InputOn { get; set; }

        /// <summary>The card in the card info panel (ShowCard): a Konami id, 2 = "the current card", or null.</summary>
        public int? CardInfo { get; set; }
        public List<int> AllowedCards { get; } = [];
        public List<string> DeckStacks { get; } = [];
        public string? Ended { get; set; }
    }

    /// <summary>
    /// Plays a tutorial frame by frame the way YGO::TUTORIAL::AdvanceStep/ExecuteStep step through it (60 frames a second). What the
    /// player does isn't simulated: a message stays up for <see cref="MessageFrames"/> and a WaitForAction for <see cref="ActionFrames"/>,
    /// then the tutorial goes on as if they'd clicked / done it. Every other step takes no time except Wait (P2 frames). Labels do
    /// nothing; RetryEnd always carries on (the game's counter starts at 1); End stops.
    /// </summary>
    public sealed class TutorialPlayback
    {
        public const int FramesPerSecond = 60;
        private const int MaxSteps = 20000;

        private readonly TutorialFile _file;
        private readonly List<(int Frame, int Step)> _starts = [];

        public TutorialPlayback(TutorialFile file, int messageFrames = 150, int actionFrames = 90)
        {
            _file = file;
            MessageFrames = Math.Max(1, messageFrames);
            ActionFrames = Math.Max(1, actionFrames);
            Build();
        }

        public int MessageFrames { get; }
        public int ActionFrames { get; }

        /// <summary>The frame the last step ends on.</summary>
        public int TotalFrames { get; private set; }

        /// <summary>(frame the step starts on, step index) in playing order; a step can appear more than once, or never.</summary>
        public IReadOnlyList<(int Frame, int Step)> Starts => _starts;

        /// <summary>The first frame a step is played on, or -1 if playback never reaches it.</summary>
        public int FirstFrameOf(int step)
        {
            foreach (var (frame, s) in _starts)
                if (s == step)
                    return frame;
            return -1;
        }

        /// <summary>How long a step keeps the tutorial there, in frames.</summary>
        public int Duration(TutorialStep step) => step.Op switch
        {
            TutorialOp.Message => MessageFrames,
            TutorialOp.WaitForAction => ActionFrames,
            TutorialOp.Wait => step.P2,
            _ => 0,
        };

        private void Build()
        {
            int frame = 0, index = 0;
            for (int count = 0; count < MaxSteps && index >= 0 && index < _file.Steps.Count; count++)
            {
                var step = _file.Steps[index];
                _starts.Add((frame, index));
                frame += Duration(step);
                if (step.Op == TutorialOp.End)
                    break;
                index = Next(index);
            }
            TotalFrames = frame;
        }

        private int Next(int index)
        {
            var step = _file.Steps[index];
            if (step.Op == TutorialOp.Goto)
            {
                int label = _file.Steps.FindIndex(s => s.Op == TutorialOp.Label && s.P1 == step.P2);
                if (label >= 0)
                    return label;
            }
            return index + 1;
        }

        /// <summary>The index into <see cref="Starts"/> of the step on screen at a frame (the last one started by then).</summary>
        public int StartIndexAt(int frame)
        {
            int found = 0;
            for (int i = 0; i < _starts.Count && _starts[i].Frame <= frame; i++)
                found = i;
            return found;
        }

        /// <summary>The screen at a frame (0..TotalFrames): replays every step up to it.</summary>
        public TutorialState StateAt(int frame) => StateAt(frame, int.MaxValue);

        /// <summary>
        /// The screen at a frame, replaying the played steps only up to Starts[lastStart] (steps that take no time share a frame, so
        /// this is how the preview stops between them).
        /// </summary>
        public TutorialState StateAt(int frame, int lastStart)
        {
            var state = new TutorialState();
            frame = Math.Clamp(frame, 0, TotalFrames);
            for (int i = 0; i < _starts.Count && i <= lastStart; i++)
            {
                var (start, stepIndex) = _starts[i];
                if (start > frame)
                    break;
                // a step with no duration that's followed by another on the same frame still applies its effect
                Apply(state, _file.Steps[stepIndex], start);
                state.Step = stepIndex;
                state.StepFrame = frame - start;
            }
            state.Frame = frame;
            if (state.Pointer != null && state.PointerUntil > 0 && frame >= state.PointerUntil)
                state.Pointer = null;
            return state;
        }

        private void Apply(TutorialState state, TutorialStep step, int start)
        {
            // what a step leaves on screen goes when the next message/wait takes over
            if (step.Op is TutorialOp.Message or TutorialOp.WaitForAction or TutorialOp.Wait)
            {
                state.Message = null;
                state.Waiting = null;
            }
            state.PhaseBanner = false;
            string? text = _file.TextOf(step);
            switch (step.Op)
            {
                case TutorialOp.SetupScenario:
                    state.Scenario = step.P2;
                    state.Phase = -1;
                    state.TurnPlayer = 0;
                    state.AllowedCards.Clear();
                    state.DeckStacks.Clear();
                    break;
                case TutorialOp.Message:
                    state.Message = text ?? "";
                    state.MessageUpper = step.P3 != 0;
                    state.Hint = null;   // YGO::TUTORIAL::ShowMessage hides the hint line
                    break;
                case TutorialOp.Hint:
                    state.Hint = string.IsNullOrEmpty(text) ? null : text;
                    break;
                case TutorialOp.SetTurnPlayer:
                    state.TurnPlayer = step.P2 & 1;
                    break;
                case TutorialOp.Phase:
                    state.Phase = step.P2;
                    state.PhaseBanner = true;
                    break;
                case TutorialOp.MoveCursor:
                    state.Cursor = (step.P1, step.P2, step.P3);
                    break;
                case TutorialOp.WaitForAction:
                    state.Waiting = TutorialFile.ActionName(step.P1);
                    if (!string.IsNullOrEmpty(text))
                        state.Hint = text;
                    break;
                case TutorialOp.Pointer:
                    state.Pointer = (step.P1, step.P2);
                    state.PointerUntil = step.P3 > 0 ? start + step.P3 : 0;
                    break;
                case TutorialOp.PointerSimple:
                    state.Pointer = step.P1 == 0 ? null : (0, step.P1);
                    state.PointerUntil = 0;
                    break;
                case TutorialOp.ShowCard:
                    state.CardInfo = step.P2 == 0 ? null : step.P2;
                    break;
                case TutorialOp.AllowInput:
                    state.InputOn = step.P2 != 0;
                    break;
                case TutorialOp.AllowCards:
                    state.AllowedCards.Clear();
                    foreach (int card in new[] { step.P2, step.P3, step.P4 })
                        if (card != 0)
                            state.AllowedCards.Add(card);
                    break;
                case TutorialOp.StackDeck:
                    state.DeckStacks.Add($"player {step.P2 & 1} deck[{step.P3}] = ${step.P4}");
                    break;
                case TutorialOp.End:
                    state.Ended = TutorialFile.Describe(step);
                    break;
            }
        }
    }
}
