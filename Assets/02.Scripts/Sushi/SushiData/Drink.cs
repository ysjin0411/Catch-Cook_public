using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>공통 메뉴 정보를 상속받아 음료 제공 설정을 관리합니다.</summary>
[CreateAssetMenu(fileName = "Drink", menuName = "Game/Sushi/Drink")]
public class Drink : Sushi
{
    [Header("음료 제공 시간")]
    [SerializeField, Min(0.1f)] float _drinkInteractionTime = 3f;

    /// <summary>클래스에 따라 메뉴를 음료로 구분합니다.</summary>
    public override MenuType Type => MenuType.Drink;
    /// <summary>손님에게 음료를 제공하는 데 필요한 시간을 반환합니다.</summary>
    public override float DrinkInteractionTime => _drinkInteractionTime;
    /// <summary>음료는 쿠커의 조리 과정을 사용하지 않습니다.</summary>
    public override float CookTime => 0f;
    /// <summary>음료는 음식 조리용 재료를 요구하지 않습니다.</summary>
    public override IReadOnlyList<IngredientRequirement> Ingredients =>
        Array.Empty<IngredientRequirement>();

    /// <summary>공통 메뉴 설정과 음료 제공 시간을 검증합니다.</summary>
    protected override void OnValidate()
    {
        // ID, 보상 및 대기 시간 등 공통 값을 검증합니다.
        base.OnValidate();

        // 음수, NaN, 무한대 등 잘못된 제공 시간을 기본값으로 복구합니다.
        if (!(_drinkInteractionTime >= 0.1f) || float.IsInfinity(_drinkInteractionTime))
            _drinkInteractionTime = 3f;
    }
}
