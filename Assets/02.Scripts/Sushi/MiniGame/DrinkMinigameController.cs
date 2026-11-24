using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SushiPouringV3
{
    /// <summary>게임 규칙과 UI를 분리한 채 키 연타와 라운드 생명주기를 연결합니다.</summary>
    public sealed class DrinkMinigameController : MonoBehaviour
    {
        // 진행 시간, 게이지, 유량과 입자 설정을 가진 밸런스 에셋입니다.
        [SerializeField] FluidPourConfig _config;
        // 새 누름마다 회복할 키이며 기본은 F입니다. 길게 누르기는 반복되지 않습니다.
        [SerializeField] Key _tapKey = Key.F;
        // true면 Time.timeScale과 무관한 시간으로 진행합니다.
        [SerializeField] bool _useUnscaledTime;
        // 독립 테스트할 때만 활성화 시 자동 시작하도록 설정합니다.
        [SerializeField] bool _beginOnEnable;
        // View에서 별도로 전달하지 않았을 때 사용할 계산 좌표의 병 입구입니다.
        [SerializeField] Vector2 _emitterPosition = new Vector2(0.45f, 1.25f);
        // 기본 발사 방향이며 시작 시 정규화합니다.
        [SerializeField] Vector2 _emitterDirection = new Vector2(-0.5f, -0.8660254f);
        // 결과 UI 연결 전 Console에서 결과를 확인할지 정합니다.
        [SerializeField] bool _logResults = true;
        // 시작 키 해제와 새 누름을 구분하는 입력 상태입니다.
        readonly FluidTapInput _tapInput = new FluidTapInput();
        // 현재 라운드와 View에서 갱신할 실제 병 입구입니다.
        FluidPourSession _session;
        FluidEmitterPose _emitter;
        // 준비된 화면을 유지하면서 입력과 시간 진행만 별도로 잠급니다.
        bool _roundStarted;

        public bool IsPlaying => _roundStarted && _session != null && _session.IsRunning;
        public bool IsPrepared => !_roundStarted && _session != null && _session.IsRunning;

        /// <summary>라운드 시작 전에 사용할 컵과 게임 설정을 변경합니다.</summary>
        public bool TrySetConfig(FluidPourConfig config)
        {
            // 진행 중인 라운드나 비어 있는 설정은 교체하지 않습니다.
            if (IsPlaying || IsPrepared || config == null) return false;
            // 다음 BeginRound에서 새 세션을 만들 때 사용할 설정을 보관합니다.
            _config = config;
            // 설정 교체 성공을 호출자에게 알립니다.
            return true;
        }

        /// <summary>입력과 시간이 정지된 새 세션을 화면에 전달합니다.</summary>
        public event Action<FluidPourSession> Prepared;
        /// <summary>준비된 세션의 실제 플레이가 시작되었을 때 전달합니다.</summary>
        public event Action<FluidPourSession> Started;
        /// <summary>라운드가 종료되면 View 또는 매장 연결 측에 결과를 전달합니다.</summary>
        public event Action<FluidPourSession, FluidCompletion> Completed;

        /// <summary>독립 테스트 설정이 켜져 있으면 라운드를 시작합니다.</summary>
        void OnEnable()
        {
            // 실제 매장 연결에서는 BeginRound를 직접 호출합니다.
            if (_beginOnEnable && !IsPrepared) BeginRound();
        }

        /// <summary>Inspector 설정으로 새 라운드를 만들고 입력을 준비합니다.</summary>
        public bool BeginRound()
        {
            // 기존 즉시 시작 호출은 준비와 진행 시작을 연속해서 수행합니다.
            if (!isActiveAndEnabled) return false;
            return PrepareRound() && StartPreparedRound();
        }

        /// <summary>입력과 타이머를 진행하지 않고 화면에 사용할 세션을 준비합니다.</summary>
        public bool PrepareRound()
        {
            // 진행 중 중복 시작과 비활성 컨트롤러의 시작 요청은 거부합니다.
            if (!Application.isPlaying || !gameObject.activeInHierarchy || IsPlaying || IsPrepared) return false;
            if (_config == null)
            {
                Debug.LogError("음료 미니게임의 Config에 밸런스 에셋을 연결하세요.", this);
                return false;
            }
            try
            {
                // 새 설정이 유효한지 확인한 뒤 기존 완료 라운드 참조를 교체합니다.
                FluidPourSession next = _config.CreateSession();
                FluidEmitterPose emitter = new FluidEmitterPose(
                    _emitterPosition.x, _emitterPosition.y, _emitterDirection.x, _emitterDirection.y);
                if (_session != null) _session.Completed -= OnSessionCompleted;
                _session = next;
                // 세션 생성만으로는 Update에서 시간이 흐르지 않게 합니다.
                _roundStarted = false;
                _emitter = emitter;
                _tapInput.Reset();
                _session.Completed += OnSessionCompleted;
            }
            catch (ArgumentException exception)
            {
                // 잘못된 밸런스를 조용히 기본값으로 바꾸지 않고 알립니다.
                Debug.LogException(exception, this);
                return false;
            }
            // 모든 내부 준비가 끝난 뒤에만 View가 세션을 받습니다.
            Action<FluidPourSession> handler = Prepared;
            if (handler != null) handler(_session);
            return IsPrepared;
        }

        /// <summary>준비한 세션을 유지한 채 입력과 타이머만 시작합니다.</summary>
        public bool StartPreparedRound()
        {
            // 취소되거나 이미 시작된 라운드는 다시 시작하지 않습니다.
            if (!Application.isPlaying || !isActiveAndEnabled || !IsPrepared) return false;
            // 대기 중 누른 키는 버리고 새 입력부터 처리합니다.
            _tapInput.Reset();
            _roundStarted = true;
            // 실제 시작 알림과 효과음은 Ready/Start가 끝난 뒤 한 번만 발생합니다.
            Started?.Invoke(_session);
            GameSfx.Play(GameSoundId.DrinkStart);
            return true;
        }

        /// <summary>선택한 키의 새 누름과 프레임 시간을 세션에 전달합니다.</summary>
        void Update()
        {
            // 세션을 로컬로 보관하여 종료 콜백에서 새 라운드가 시작되어도 섞이지 않게 합니다.
            FluidPourSession session = _session;
            if (session == null || !_roundStarted) return;
            double dt = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt <= 0d)
            {
                // 일시정지 중 잡힌 키를 재개 시 연타로 인식하지 않습니다.
                _tapInput.Reset();
                return;
            }
            if (session.IsRunning)
            {
                // Keyboard가 없는 환경에서도 예외 없이 입력 없음으로 처리합니다.
                Keyboard keyboard = Keyboard.current;
                bool held = keyboard != null && _tapKey != Key.None && keyboard[_tapKey].isPressed;
                if (_tapInput.Read(held)) session.Tap();
            }
            // 이 컨트롤러를 사용하는 View는 Advance를 별도로 호출하지 않습니다.
            session.Advance(dt, _emitter);
        }

        /// <summary>View가 현재 세션의 읽기 전용 상태를 조회할 수 있게 전달합니다.</summary>
        public bool TryGetSession(out FluidPourSession session)
        {
            // 시작 전에는 false이며 출력 참조를 사용하지 않습니다.
            session = _session;
            return session != null;
        }

        /// <summary>View가 화면 좌표를 변환한 실제 병 입구를 전달합니다.</summary>
        public void SetEmitterPose(FluidEmitterPose emitter)
        {
            // 방향이 없는 default 구조체는 세션에 전달하지 않습니다.
            if (!emitter.IsValid) throw new ArgumentException("병 입구 방향이 유효하지 않습니다.", nameof(emitter));
            _emitter = emitter;
        }

        /// <summary>손님 이탈, 화면 종료 등 외부 사유로 라운드를 취소합니다.</summary>
        public void CancelRound()
        {
            // 준비 중 취소도 입력과 시간 진행을 확실히 차단합니다.
            _roundStarted = false;
            // 세션이 이미 종료됐으면 내부에서 중복 호출을 무시합니다.
            if (_session != null) _session.Cancel();
        }

        /// <summary>세션 결과를 외부로 전달하고 내부 구독을 정리합니다.</summary>
        void OnSessionCompleted(FluidPourSession sender, FluidCompletion result)
        {
            // 오래된 세션을 보관하는 View가 컨트롤러를 계속 붙잡지 않게 합니다.
            sender.Completed -= OnSessionCompleted;
            if (!object.ReferenceEquals(sender, _session)) return;
            if (_logResults)
                Debug.Log($"음료 미니게임: {result.Result}, 생성량 {result.GeneratedVolume:F3}, 목표 {result.TargetVolume:F3}", this);
            // 실제 보상과 서빙 권한 확인은 매장 연결 측에서 처리합니다.
            Action<FluidPourSession, FluidCompletion> handler = Completed;
            if (handler != null) handler(sender, result);
            if (result.Result == FluidResult.TooLittle || result.Result == FluidResult.TooMuch)
                GameSfx.Play(GameSoundId.Rejected);
        }

        /// <summary>비활성화되면 진행 중인 라운드와 구독을 정리합니다.</summary>
        void OnDisable()
        {
            // 취소 이벤트는 구독을 제거하기 전에 전달합니다.
            FluidPourSession session = _session;
            if (session == null) return;
            try { session.Cancel(); }
            finally { session.Completed -= OnSessionCompleted; }
        }

        /// <summary>창 포커스를 잃으면 의도하지 않은 입력 진행을 취소합니다.</summary>
        void OnApplicationFocus(bool focused)
        {
            // 복귀 시 누르고 있던 키가 새 누름으로 남지 않게 합니다.
            if (!focused) CancelRound();
        }
    }
}
