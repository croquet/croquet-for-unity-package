using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

[HelpURL("https://forum.unity.com/threads/finally-a-serializable-dictionary-for-unity-extracted-from-system-collections-generic.335797/page-2")]
public abstract class DictionaryDrawer<TK, TV> : PropertyDrawer
{
    private SerializableDictionary<TK, TV> _Dictionary;
    private bool _Foldout;
    private const float kButtonWidth = 22f;
    private const int kMargin = 4;

    static readonly GUIContent iconToolbarMinus = EditorGUIUtility.IconContent("Toolbar Minus", "Remove selection from list");
    static readonly GUIContent iconToolbarPlus = EditorGUIUtility.IconContent("Toolbar Plus", "Add to list");
    static readonly GUIStyle preButton = "RL FooterButton";
    static readonly GUIStyle boxBackground = "RL Background";


    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        CheckInitialize(property, label);
        if (_Foldout)
            return Mathf.Max((_Dictionary.Count + 1) * 17f, 17 + 16) + kMargin * 2;
        return 17f + kMargin * 2;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        CheckInitialize(property, label);

        var backgroundRect = position;
        backgroundRect.xMin -= 17;
        backgroundRect.height += kMargin;
        if (Event.current.type == EventType.Repaint)
            boxBackground.Draw(backgroundRect, false, false, false, false);

        position.y += kMargin;
        position.height = 17f;

        var foldoutRect = position;
        foldoutRect.width -= 2 * kButtonWidth;
        EditorGUI.BeginChangeCheck();
        _Foldout = EditorGUI.Foldout(foldoutRect, _Foldout, label, true);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetBool(label.text, _Foldout);

        position.xMin += kMargin;
        position.xMax -= kMargin;

        var buttonRect = position;
        buttonRect.xMin = position.xMax - kButtonWidth;

        if (GUI.Button(buttonRect, iconToolbarMinus, preButton))
        {
            ClearDictionary();
        }

        buttonRect.x -= kButtonWidth - 1;

        if (GUI.Button(buttonRect, iconToolbarPlus, preButton))
        {
            AddNewItem();
        }

        if (!_Foldout)
            return;

        var labelRect = position;
        labelRect.y += 16;
        if (_Dictionary.Count == 0)
            GUI.Label(labelRect, "This dictionary doesn't have any items. Click + to add one!");

        foreach (var item in _Dictionary)
        {
            var key = item.Key;
            var value = item.Value;

            position.y += 17f;

            var keyRect = position;
            keyRect.width /= 2;
            keyRect.width -= 4;
            EditorGUI.BeginChangeCheck();
            var newKey = DoField(keyRect, typeof(TK), key);
            if (EditorGUI.EndChangeCheck())
            {
                try
                {
                    _Dictionary.Remove(key);
                    _Dictionary.Add(newKey, value);
                }
                catch (Exception e)
                {
                    Debug.Log(e.Message);
                }
                break;
            }

            var valueRect = position;
            valueRect.xMin = keyRect.xMax;
            valueRect.xMax = position.xMax - kButtonWidth;
            EditorGUI.BeginChangeCheck();
            value = DoField(valueRect, typeof(TV), value);
            if (EditorGUI.EndChangeCheck())
            {
                _Dictionary[key] = value;
                break;
            }

            var removeRect = position;
            removeRect.xMin = removeRect.xMax - kButtonWidth;
            if (GUI.Button(removeRect, iconToolbarMinus, preButton))
            {
                RemoveItem(key);
                break;
            }
        }
    }

    private void RemoveItem(TK key)
    {
        _Dictionary.Remove(key);
    }

    private void CheckInitialize(SerializedProperty property, GUIContent label)
    {
        if (_Dictionary == null)
        {
            var target = property.serializedObject.targetObject;
            _Dictionary = fieldInfo.GetValue(target) as SerializableDictionary<TK, TV>;
            if (_Dictionary == null)
            {
                _Dictionary = new SerializableDictionary<TK, TV>();
                fieldInfo.SetValue(target, _Dictionary);
            }

            _Foldout = EditorPrefs.GetBool(label.text);
        }
    }

    private static readonly Dictionary<Type, Func<Rect, object, object>> _Fields =
        new Dictionary<Type, Func<Rect, object, object>>()
        {
            { typeof(int), (rect, value) => EditorGUI.IntField(rect, (int)value) },
            { typeof(float), (rect, value) => EditorGUI.FloatField(rect, (float)value) },
            { typeof(string), (rect, value) => EditorGUI.TextField(rect, (string)value) },
            { typeof(bool), (rect, value) => EditorGUI.Toggle(rect, (bool)value) },
            { typeof(Vector2), (rect, value) => EditorGUI.Vector2Field(rect, GUIContent.none, (Vector2)value) },
            { typeof(Vector3), (rect, value) => EditorGUI.Vector3Field(rect, GUIContent.none, (Vector3)value) },
            { typeof(Bounds), (rect, value) => EditorGUI.BoundsField(rect, (Bounds)value) },
            { typeof(Rect), (rect, value) => EditorGUI.RectField(rect, (Rect)value) },
        };

    private static T DoField<T>(Rect rect, Type type, T value)
    {
        if (_Fields.TryGetValue(type, out Func<Rect, object, object> field))
            return (T)field(rect, value);

        if (type.IsEnum)
            return (T)(object)EditorGUI.EnumPopup(rect, (Enum)(object)value);

        if (typeof(UnityObject).IsAssignableFrom(type))
            return (T)(object)EditorGUI.ObjectField(rect, (UnityObject)(object)value, type, true);

        Debug.Log("Type is not supported: " + type);
        return value;
    }

    private void ClearDictionary()
    {
        _Dictionary.Clear();
    }

    private void AddNewItem()
    {
        TK key;
        if (typeof(TK) == typeof(string))
            key = (TK)(object)"";
        else key = default;

        var value = default(TV);
        try
        {
            _Dictionary.Add(key, value);
        }
        catch (Exception e)
        {
            Debug.Log(e.Message);
        }
    }
}

[CustomPropertyDrawer(typeof(StringStringSerializableDict))]
public class StringStringDictDrawer : DictionaryDrawer<string, string> { }