using UnityEngine;

/// <summary>
/// 요리 강화에 사용하는 전용 재료 아이템의 데이터를 보관합니다.
/// </summary>
[CreateAssetMenu(
    fileName = "IngredientItemData",
    menuName = "Game/Item/Sushi Upgrade Ingredient")]
public class IngredientItemData : ItemData
{
    // 강화 재료의 아이템 카테고리를 제공합니다.
    public override ItemCategory Category => ItemCategory.Ingredient;

    [Header("획득처 정보")]
    [SerializeField] string _getDesc;


    public string GetDesc => _getDesc;
}