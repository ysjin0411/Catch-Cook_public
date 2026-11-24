using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(FishSpawnLayerAttribute))]
public sealed class FishSpawnLayerDrawer : PropertyDrawer
{
    /// <summary>레이어 마스크가 아닌 단일 Layer를 선택하는 기본 드롭다운을 만듭니다.</summary>
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        // SerializedProperty 바인딩으로 Undo와 프리팹 오버라이드를 유지합니다.
        LayerField field = new LayerField(property.displayName);
        field.BindProperty(property);
        return field;
    }
}
