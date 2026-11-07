using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Defense90Tests
{
    public sealed class DefensePrefabIntegrityTests
    {
        [Test]
        public void EveryDefenseWindowHasSerializableComponents()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/BundleAssets/UI/Defense" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), path + "/" + child.name);
            }
        }

        [Test]
        public void UguiAtlasDoesNotRotateOrTightlyPackSprites()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>("Assets/AssetArt/Atlas/Atlas_Defense_UI.spriteatlasv2");
            var packing = atlas.GetPackingSettings();
            Assert.IsFalse(packing.enableRotation, "uGUI Image does not support rotated atlas sprites");
            Assert.IsFalse(packing.enableTightPacking, "Sliced UI requires rectangular sprite regions");
        }

        [UnityTest]
        public IEnumerator UiAtlasImportRepairsPackingWithoutChangingGuid()
        {
            const string path = "Assets/AssetArt/Atlas/Atlas_Defense_UI.spriteatlasv2";
            var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(path);
            var original = importer.packingSettings;
            string guid = AssetDatabase.AssetPathToGUID(path);
            try
            {
                var broken = original;
                broken.enableRotation = true;
                broken.enableTightPacking = true;
                importer.packingSettings = broken;
                importer.SaveAndReimport();
                // 调用与SpritePostprocessor相同的公开入口，验证实际生成结果而非仅检查辅助函数。
                var owner = Type.GetType("DGame.EditorSpriteSaveInfo, DGame.Editor", true);
                owner.GetMethod("OnImportSprite").Invoke(null, new object[]
                { "Assets/BundleAssets/UIRaw/Atlas/Defense/UI/IconHeart.png", false });
                for (int frame = 0; frame < 20; frame++)
                {
                    yield return null;
                    var saved = ((SpriteAtlasImporter)AssetImporter.GetAtPath(path)).packingSettings;
                    if (!saved.enableRotation && !saved.enableTightPacking) break;
                }
                var packing = ((SpriteAtlasImporter)AssetImporter.GetAtPath(path)).packingSettings;
                Assert.IsFalse(packing.enableRotation, "重复导入不得重新开启旋转");
                Assert.IsFalse(packing.enableTightPacking, "重复导入不得重新开启紧密打包");
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
            }
            finally
            {
                importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(path);
                importer.packingSettings = original;
                importer.SaveAndReimport();
            }
        }

        [Test]
        public void NonUiAtlasRetainsConfiguredPackingOptions()
        {
            var owner = Type.GetType("DGame.EditorSpriteSaveInfo, DGame.Editor", true);
            var config = owner.GetProperty("Config", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var policy = owner.GetMethod("CreatePackingSettings", BindingFlags.Static | BindingFlags.NonPublic);
            var packing = (SpriteAtlasPackingSettings)policy.Invoke(null, new object[]
            { new[] { "Assets/BundleAssets/Actor/FrameSprite/Example.png" } });
            Assert.AreEqual(config.GetType().GetField("enableRotation").GetValue(config), packing.enableRotation);
            Assert.AreEqual(config.GetType().GetField("tightPacking").GetValue(config), packing.enableTightPacking);
        }

        [Test]
        public void SettingsControlsFitPanelAndDoNotOverlap()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/UI/Defense/DefenseSettingsWindow.prefab");
            var popup = root.transform.Find("m_rectSafe/m_panel/m_rectStage/m_rectPopup");
            var panel = (RectTransform)popup.Find("m_imgPanelBg");
            var controls = popup.GetComponentsInChildren<Selectable>(true);
            foreach (var control in controls)
            {
                Rect bounds = Bounds((RectTransform)control.transform, popup);
                Rect outer = Bounds(panel, popup);
                Assert.IsTrue(outer.Contains(bounds.min) && outer.Contains(bounds.max), control.name + " outside panel");
                foreach (var other in controls)
                    if (control != other)
                        Assert.IsFalse(bounds.Overlaps(Bounds((RectTransform)other.transform, popup)), control.name + " overlaps " + other.name);
            }
            Assert.NotNull(popup.Find("m_sliderMusic").GetComponent<Slider>().handleRect);
            Assert.NotNull(popup.Find("m_sliderSfx").GetComponent<Slider>().fillRect);
            Assert.AreSame(popup.Find("m_groupVfx").GetComponent<ToggleGroup>(),
                popup.Find("m_groupVfx/m_toggleVfxFull").GetComponent<Toggle>().group);
        }

        private static Rect Bounds(RectTransform rect, Transform relativeTo)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(relativeTo.InverseTransformPoint(corners[0]).x,
                relativeTo.InverseTransformPoint(corners[0]).y,
                relativeTo.InverseTransformPoint(corners[2]).x,
                relativeTo.InverseTransformPoint(corners[2]).y);
        }
    }
}
