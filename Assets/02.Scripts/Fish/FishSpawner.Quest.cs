using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 일반 물고기의 생애별 퀘스트 이벤트 구독과 중복 보고 방지를 담당합니다.
/// </summary>
public partial class FishSpawner
{
    /// <summary>
    /// 한 번의 스폰에 해당하는 데이터, 보고 여부와 구독 delegate를 보관합니다.
    /// </summary>
    private sealed class FishQuestEntry
    {
        public FishModel Model;
        public FishData Data;
        public Action<FishState> StateHandler;
        public Action CollectedHandler;
        public bool KillHandled;
        public bool CollectionHandled;
        public bool Closed;
    }

    // 프리뷰 전용 스포너에서는 퀘스트 집계를 끌 수 있습니다.
    [SerializeField] private bool _reportFishQuests = true;

    // 직접 배치한 일반 물고기는 한 스포너에만 명시적으로 연결합니다.
    [SerializeField] private Fish[] _sceneFish = Array.Empty<Fish>();

    // 사망 후에도 사체 수집을 보고할 수 있도록 반환 전까지 기록을 유지합니다.
    private readonly Dictionary<Fish, FishQuestEntry> _fishQuestEntries =
        new Dictionary<Fish, FishQuestEntry>();

    // 종료 중에는 이벤트 재진입으로 새 기록이 등록되지 않도록 막습니다.
    private bool _fishQuestsClosing;

    /// <summary>
    /// 씬에 직접 배치한 물고기를 초기화하여 스폰 개체와 같은 생명주기에 연결합니다.
    /// </summary>
    private void InitializeSceneFish()
    {
        // 직접 배치한 목록이 없는 스포너는 일반 패턴 스폰만 수행합니다.
        if (_sceneFish == null) return;

        // 명시적으로 지정된 개체만 처리하므로 배경 물고기나 다른 씬을 검색하지 않습니다.
        foreach (Fish fish in _sceneFish)
        {
            // 이미 초기화된 개체는 중복 초기화하거나 다른 스포너에서 가져오지 않습니다.
            if (fish == null || fish.Data == null || fish.OwnerSpawner != null) continue;

            // 다른 씬의 개체를 소유하지 않도록 같은 씬의 물고기만 초기화합니다.
            if (fish.gameObject.scene != gameObject.scene) continue;

            // 성공한 Initialize 내부에서 퀘스트 등록까지 함께 완료합니다.
            if (!fish.Initialize(fish.Data.InitialDirection, _hero, this)) continue;

            // 직접 배치 개체도 스포너의 전체 수직 이동 범위 안에서 움직입니다.
            fish.SetVerticalRange(new FishVerticalRange(MovementMin.y, MovementMax.y));
        }
    }

    /// <summary>
    /// 초기화가 완료된 소유 물고기의 이번 생애를 등록하고 모델 이벤트를 구독합니다.
    /// </summary>
    internal void RegisterFishQuest(Fish fish, FishModel model)
    {
        // 프리뷰, 종료 중인 스포너, 잘못된 소유 관계와 미완성 개체는 등록하지 않습니다.
        if (!_reportFishQuests || _fishQuestsClosing || fish == null || model == null
            || fish.OwnerSpawner != this || fish.Data == null || model.IsCollected) return;

        // 같은 생애의 중복 등록으로 보고 플래그를 초기화하지 않습니다.
        if (_fishQuestEntries.ContainsKey(fish)) return;

        // 데이터와 보고 플래그를 개체의 이번 생애에 고정합니다.
        FishQuestEntry entry = new FishQuestEntry { Model = model, Data = fish.Data };

        // 이전 생애의 지연 콜백이 새 기록을 조회하지 않도록 해당 기록을 캡처합니다.
        entry.StateHandler = state =>
        {
            // 상태 변경을 처치 보고 처리로 전달합니다.
            HandleFishQuestState(entry, state);
        };
        entry.CollectedHandler = () =>
        {
            // 이벤트와 직접 수집 알림이 동일한 중복 방지 처리를 사용합니다.
            HandleFishQuestCollection(entry);
        };

        // 이벤트가 실행되기 전에 관리 기록을 등록합니다.
        _fishQuestEntries.Add(fish, entry);

        // 해제 시에도 동일한 delegate를 사용하도록 보관한 핸들러를 구독합니다.
        model.StateChanged += entry.StateHandler;
        model.Collected += entry.CollectedHandler;
    }

    /// <summary>
    /// 최초 사망을 처치로 보고하고 수집 기록을 남긴 채 모델 이벤트를 해제합니다.
    /// </summary>
    private void HandleFishQuestState(FishQuestEntry entry, FishState state)
    {
        // 종료된 생애와 수집 완료 개체, 사망 이외의 상태 및 중복 사망을 무시합니다.
        if (entry.Closed || entry.KillHandled || entry.CollectionHandled
            || state != FishState.Dead) return;

        // 외부 퀘스트 이벤트가 재진입하더라도 한 번만 전달하도록 먼저 표시합니다.
        entry.KillHandled = true;
        try
        {
            // 현재 게임 매니저를 사용하며 반환값은 전달 성공 여부로만 해석합니다.
            GameManager manager = GameManager.Instance;
            if (manager == null || !manager.ReportFishKilled(entry.Data))
            {
                // 실패를 숨기지 않되 중복 집계 위험이 있는 자동 재시도는 하지 않습니다.
                Debug.LogWarning("물고기 처치 퀘스트 전달에 실패했습니다.", this);
            }
        }
        catch (Exception exception)
        {
            // 퀘스트 구독자의 예외가 물고기의 사망 처리를 중단하지 않도록 기록합니다.
            Debug.LogException(exception, this);
        }
        finally
        {
            // 사망 시 구독은 모두 해제하지만 사체 수집을 위한 기록은 남겨 둡니다.
            UnsubscribeFishQuest(entry);
        }
    }

    /// <summary>
    /// 실제 수집이 확정된 물고기를 풀 반환 전에 스포너의 수집 보고로 연결합니다.
    /// </summary>
    internal void NotifyFishQuestCollected(Fish fish)
    {
        // 미등록 개체와 이미 반환되어 기록이 사라진 개체는 보고하지 않습니다.
        if (fish == null || !_fishQuestEntries.TryGetValue(fish, out FishQuestEntry entry)) return;

        // 사망으로 구독이 해제된 개체도 같은 중복 방지 경로를 사용합니다.
        HandleFishQuestCollection(entry);
    }

    /// <summary>
    /// 살아 있는 포획과 사체 수집을 생애당 한 번 보고하고 모델 이벤트를 해제합니다.
    /// </summary>
    private void HandleFishQuestCollection(FishQuestEntry entry)
    {
        // 수집 확정 이전 호출과 종료된 생애, 중복 수집 알림을 무시합니다.
        if (entry.Closed || entry.CollectionHandled || entry.Model == null
            || !entry.Model.IsCollected) return;

        // 이벤트 구독 순서와 관계없이 한 번만 전달하도록 호출 전에 표시합니다.
        entry.CollectionHandled = true;
        try
        {
            // 반환 전에 보관한 데이터를 현재 게임 매니저로 전달합니다.
            GameManager manager = GameManager.Instance;
            if (manager == null || !manager.ReportFishCollected(entry.Data))
            {
                // 전달 실패가 실제 수집과 풀 반환을 막지 않도록 경고만 남깁니다.
                Debug.LogWarning("물고기 수집 퀘스트 전달에 실패했습니다.", this);
            }
        }
        catch (Exception exception)
        {
            // 퀘스트 구독자의 예외가 TryCollect와 가방 지급을 중단하지 않도록 기록합니다.
            Debug.LogException(exception, this);
        }
        finally
        {
            // 사망 시 이미 해제했어도 동일한 delegate의 재해제는 안전합니다.
            UnsubscribeFishQuest(entry);
        }
    }

    /// <summary>
    /// 등록할 때 저장한 delegate로 해당 생애의 모델 이벤트 연결을 해제합니다.
    /// </summary>
    private void UnsubscribeFishQuest(FishQuestEntry entry)
    {
        // 파괴된 Unity 객체의 관리 이벤트도 정리할 수 있도록 실제 null만 검사합니다.
        if (ReferenceEquals(entry.Model, null)) return;

        // 새 람다를 만들지 않고 구독에 사용한 delegate를 그대로 제거합니다.
        entry.Model.StateChanged -= entry.StateHandler;
        entry.Model.Collected -= entry.CollectedHandler;
    }

    /// <summary>
    /// 풀 반환 또는 파괴 시 구독과 해당 생애의 관리 기록을 함께 제거합니다.
    /// </summary>
    internal void UnregisterFishQuest(Fish fish)
    {
        // 파괴 중인 개체도 사전에서 제거할 수 있도록 실제 null만 검사합니다.
        if (ReferenceEquals(fish, null)
            || !_fishQuestEntries.TryGetValue(fish, out FishQuestEntry entry)) return;

        // 현재 이벤트 호출 목록에 남은 이전 생애의 콜백을 차단합니다.
        entry.Closed = true;

        // 중복 해제가 안전하도록 관리 기록을 먼저 제거합니다.
        _fishQuestEntries.Remove(fish);

        // 사망 또는 수집 이후 남은 이벤트 연결도 정리합니다.
        UnsubscribeFishQuest(entry);
    }

    /// <summary>
    /// 스포너 종료 시 추가 등록을 막고 모든 물고기의 퀘스트 연결을 정리합니다.
    /// </summary>
    private void ClearFishQuestEntries()
    {
        // 종료 중 발생하는 재진입 등록을 차단합니다.
        _fishQuestsClosing = true;

        // 관리 중인 모든 생애를 닫고 구독을 해제합니다.
        foreach (FishQuestEntry entry in _fishQuestEntries.Values)
        {
            // 이미 실행 중인 이벤트 콜백에서도 보고하지 못하도록 표시합니다.
            entry.Closed = true;

            // 모델에 남은 이벤트 연결을 제거합니다.
            UnsubscribeFishQuest(entry);
        }

        // 물고기와 데이터에 대한 관리 참조를 모두 제거합니다.
        _fishQuestEntries.Clear();
    }
}
