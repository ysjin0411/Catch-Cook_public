using System;
using System.Collections.Generic;
using UnityEngine;

public enum MenuType
{
    Food = 0, // 쿠커에게 조리받아 운반하는 음식입니다.
    Drink = 1 // 손님 앞에서 연속 상호작용하여 제공하는 음료입니다.
}

[CreateAssetMenu(fileName = "Sushi", menuName = "Game/Sushi/Sushi")]
public class Sushi : ScriptableObject
{
    /// <summary>기본 메뉴인 음식을 반환하며 음료는 자식 클래스에서 재정의합니다.</summary>
    public virtual MenuType Type => MenuType.Food;
    /// <summary>음식에서는 사용하지 않는 음료 제공 시간의 기본값입니다.</summary>
    public virtual float DrinkInteractionTime => 0f;
    [Serializable]
    public class IngredientRequirement
    {
        [SerializeField] ItemData _item;
        [SerializeField, Min(1)] int _amount = 1;

        public ItemData Item => _item;
        public int Amount => _amount;

        /// <summary>
        /// 스시 하나에 필요한 재료 수량을 최소 1개로 제한합니다.
        /// </summary>
        internal void Validate()
        {
            // 등록한 재료의 요구량이 0 이하가 되지 않도록 제한합니다.
            _amount = Mathf.Max(1, _amount);
        }
    }

    [Header("고유 ID")]
    [SerializeField, Min(0)] int _id;

    [Header("이름")]
    [SerializeField] string _name;

    [Header("아이콘")]
    [SerializeField] Sprite _icon;

    [Header("아이템 설명")]
    [SerializeField][TextArea(3,5)] string _desc;

    [Header("메뉴 외형")]
    // 조리 완료, 운반, 식사 중에 표시할 음식 또는 음료 프리팹입니다.
    [SerializeField] GameObject _visualPrefab;
    // 식사 완료 후 표시할 잔반 또는 빈 잔이며, 비워 두면 내용물을 숨깁니다.
    [SerializeField] GameObject _remainingPrefab;

    [Header("보상 골드")]
    [SerializeField, Min(0f)] float _gold;

    [Header("보상 경험치")]
    [SerializeField, Min(0)] int _playerExperience;

    [Header("만드는데 필요한 재료")]
    [SerializeField] IngredientRequirement[] _ingredients;

    [Header("요리하는데 걸리는 시간")]
    [SerializeField, Min(0f)] float _cookTime;

    [Header("이 음식을 손님이 기다리는 시간")]
    [SerializeField, Min(0f)] float _waitingTime = 30f;

    [Header("손님의 식사 시간")]
    [SerializeField, Min(0f)] float _eatingTime = 5f;

    public int Id => _id;
    public string Name => _name;
    /// <summary>요리의 상세 설명입니다.</summary>
    public string Description => _desc;
    public float Gold => _gold;
    public int PlayerExperience => _playerExperience;
    public Sprite Icon => _icon;
    // 표시 컴포넌트가 사용할 기본 메뉴 외형을 제공합니다.
    public GameObject VisualPrefab => _visualPrefab;
    // 표시 컴포넌트가 사용할 식사 후 외형을 제공합니다.
    public GameObject RemainingPrefab => _remainingPrefab;
    public virtual float CookTime => _cookTime;
    public float WaitingTime => _waitingTime;
    public float EatingTime => _eatingTime;

    public virtual IReadOnlyList<IngredientRequirement> Ingredients =>
        Array.AsReadOnly(_ingredients ?? Array.Empty<IngredientRequirement>());

    /// <summary>
    /// 데이터의 ID, 보상, 시간과 재료 요구량을 검증합니다.
    /// </summary>
    protected virtual void OnValidate()
    {
        // 고유 ID를 음수가 아닌 값으로 제한합니다.
        _id = Mathf.Max(0, _id);

        // 지급할 Gold를 음수가 아닌 값으로 제한합니다.
        _gold = Mathf.Max(0f, _gold);

        // 보상 경험치를 음수가 아닌 값으로 제한합니다.
        _playerExperience = Mathf.Max(0, _playerExperience);

        // 손님의 대기 제한시간을 음수가 아닌 값으로 제한합니다.
        _waitingTime = Mathf.Max(0f, _waitingTime);

        // 식사시간을 음수가 아닌 값으로 제한합니다.
        _eatingTime = Mathf.Max(0f, _eatingTime);

        // 음료는 음식 전용 설정의 검증에서 제외합니다.
        if (Type != MenuType.Food) return;

        // 음식의 조리 시간을 음수가 아닌 값으로 제한합니다.
        _cookTime = Mathf.Max(0f, _cookTime);

        // 재료 목록이 없으면 검증을 종료합니다.
        if (_ingredients == null) return;

        // 등록된 재료별 요구량을 확인합니다.
        foreach (IngredientRequirement ingredient in _ingredients)
        {
            // 비어 있는 항목은 제외합니다.
            if (ingredient == null) continue;

            // 재료 하나의 요구량을 검증합니다.
            ingredient.Validate();
        }
    }
}
