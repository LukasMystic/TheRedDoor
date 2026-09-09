using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TheRedDoor.Boss;
using TheRedDoor.Player;
using TheRedDoor.UI;
using TheRedDoor.World;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class PresentationPass
{
    const string Moss = "Assets/Entity/Env/Mossy Tileset/";
    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static Sprite SpriteAt(string path, string suffix) => AssetDatabase.LoadAllAssetsAtPath(path)
        .OfType<Sprite>().First(s => s.name.EndsWith(suffix, StringComparison.Ordinal));
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);

    [MenuItem("Tools/TheRedDoor/Apply Presentation Pass")]
    static void Apply()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play first.");
        var world = GameObject.Find("WorldBackground").transform;
        Undo.RegisterFullObjectHierarchyUndo(world.gameObject, "Improve background readability");
        foreach (var sr in world.Find("BackSilhouettes").GetComponentsInChildren<SpriteRenderer>())
            sr.color = Hex("#9AB4A638");
        foreach (var sr in world.Find("MidgroundPlants").GetComponentsInChildren<SpriteRenderer>())
            sr.color = Hex("#9EB79766");
        foreach (var sr in world.Find("CeilingFoliage").GetComponentsInChildren<SpriteRenderer>())
            sr.color = Hex("#94A7868C");

        var ceiling = world.Find("CeilingFoliage");
        var material = ceiling.GetComponentInChildren<SpriteRenderer>().sharedMaterial;
        for (int i = 0; i < 10; i++)
        {
            string name = "Canopy_" + (i + 1).ToString("00");
            if (ceiling.Find(name) != null) continue;
            var obj = new GameObject(name, typeof(SpriteRenderer));
            Undo.RegisterCreatedObjectUndo(obj, "Add ceiling canopy");
            obj.transform.SetParent(ceiling, false);
            var sr = obj.GetComponent<SpriteRenderer>();
            sr.sprite = SpriteAt(Moss + "Mossy - MossyHills.png", i % 2 == 0 ? "_6" : "_8");
            sr.sharedMaterial = material;
            sr.flipY = true;
            sr.flipX = i % 2 == 0;
            sr.sortingOrder = -7;
            sr.color = Hex("#809D858C");
            float scale = 0.32f + (i % 3) * 0.035f;
            obj.transform.localScale = new Vector3(scale, scale, 1f);
            obj.transform.position = new Vector3(-40 + i * 6, 5f, 0f);
            // Keep framing above the play space and attach hanging stems to a visible canopy.
            obj.transform.position += Vector3.up * ((3.4f + (i % 3) * 0.2f) - sr.bounds.min.y);
        }

        var gate = UnityEngine.Object.FindAnyObjectByType<ArenaGate>();
        Undo.RegisterFullObjectHierarchyUndo(gate.gameObject, "Dress arena gate");
        var gateArt = gate.transform.Find("GateVisual");
        if (gateArt == null)
        {
            var obj = new GameObject("GateVisual", typeof(SpriteRenderer));
            Undo.RegisterCreatedObjectUndo(obj, "Add gate artwork");
            gateArt = obj.transform;
            gateArt.SetParent(gate.transform, false);
        }
        var gateRenderer = gateArt.GetComponent<SpriteRenderer>();
        gateRenderer.sprite = SpriteAt("Assets/Entity/Env/YogePlatformerDungeon/Props/CageDoor.png", "_0");
        gateRenderer.sharedMaterial = material;
        gateRenderer.color = Hex("#C4C5ACFF");
        gateRenderer.sortingOrder = 0; // Floor order 1 hides the lowered gate.
        var box = gate.GetComponent<BoxCollider2D>();
        gateArt.localPosition = box.offset;
        var size = gateRenderer.sprite.bounds.size;
        gateArt.localScale = new Vector3(box.size.x / size.x, box.size.y / size.y, 1f);
        gate.GetComponent<SpriteRenderer>().enabled = false;

        var keeper = UnityEngine.Object.FindAnyObjectByType<KeeperController>();
        var keeperVisual = Field<SpriteRenderer>(keeper, "spriteRenderer");
        Undo.RecordObject(keeperVisual, "Remove Keeper debug tint");
        keeperVisual.color = Color.white;

        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        const string matPath = "Assets/Materials/ShockwaveAmber.mat";
        var amber = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (amber == null)
        {
            amber = new Material(Shader.Find("TheRedDoor/ShockwaveAmber"));
            AssetDatabase.CreateAsset(amber, matPath);
        }
        const string prefabPath = "Assets/Prefabs/GroundShockWave.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var sr = prefab.GetComponent<SpriteRenderer>();
            sr.color = Color.white;
            sr.sharedMaterial = amber;
            sr.sortingOrder = 12;
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        var hud = GameObject.Find("GameUI");
        Undo.RegisterFullObjectHierarchyUndo(hud, "Restyle demo typography");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Oswald Bold SDF.asset");
        var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Roboto-Bold SDF.asset");
        foreach (var label in hud.GetComponentsInChildren<TMP_Text>(true))
        {
            bool title = label.name == "BossName" || label.name.Contains("Ending");
            label.font = title ? font : bodyFont;
            label.fontSharedMaterial = label.font.material;
            label.color = Hex("#F1E8CCFF");
            label.fontStyle = FontStyles.Normal;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18;
            label.fontSizeMax = label.name == "BossName" ? 32 : Mathf.Max(26, label.fontSize);
            label.characterSpacing = title ? 6 : 0;
            label.raycastTarget = false;
            var shadow = label.GetComponent<Shadow>();
            if (shadow == null) shadow = Undo.AddComponent<Shadow>(label.gameObject);
            shadow.effectColor = new Color(0.015f, 0.03f, 0.02f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
            EditorUtility.SetDirty(label);
        }
        var controls = hud.GetComponent<TutorialControlsUI>();
        var controlSo = new SerializedObject(controls);
        controlSo.FindProperty("message").stringValue = "<color=#D6BE84>MOVE</color>  A / D or Arrow Keys\n<color=#D6BE84>JUMP</color>  Space\n<color=#D6BE84>AIR DASH</color>  Left Shift\n<color=#D6BE84>ATTACK</color>  J";
        controlSo.ApplyModifiedProperties();
        var doorSo = new SerializedObject(hud.GetComponent<RedDoorUI>());
        doorSo.FindProperty("promptMessage").stringValue = "<color=#D6BE84>[ E ]</color>  OPEN THE DOOR";
        doorSo.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(hud.scene);
        EditorSceneManager.SaveScene(hud.scene);
        AssetDatabase.SaveAssets();
        foreach (float x in new[] { -34f, -21f, -10f, 0f, 10f }) Capture(x, "polish");
        Debug.Log("PRESENTATION PASS SAVED: background, 10 canopies, gate, amber wave and typography.");
    }

    static void Capture(float x, string prefix)
    {
        var camera = Camera.main;
        var pos = camera.transform.position;
        var target = camera.targetTexture;
        var active = RenderTexture.active;
        float aspect = camera.aspect;
        var rt = new RenderTexture(1600, 900, 24);
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        try
        {
            camera.transform.position = new Vector3(x, pos.y, pos.z);
            camera.targetTexture = rt;
            camera.aspect = 16f / 9;
            camera.Render();
            RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes($"/private/tmp/thereddoor-{prefix}-{x}.png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = target;
            camera.aspect = aspect;
            camera.transform.position = pos;
            RenderTexture.active = active;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }

    static KeeperController testKeeper;
    static PlayerHealth testPlayer;
    static double testStart;
    static int testStep;
    static float leftStart, rightStart;
    static void Invoke(object obj, string name, params object[] args) => obj.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(obj, args);

    [MenuItem("Tools/TheRedDoor/Run Play Presentation Checks")]
    static void Test()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play first.");
        EditorApplication.update -= Tick;
        testKeeper = UnityEngine.Object.FindAnyObjectByType<KeeperController>();
        testPlayer = Field<PlayerHealth>(testKeeper, "target");
        testPlayer.transform.position = new Vector3(8f, -1.7f, 0f);
        testPlayer.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        testPlayer.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        Invoke(testKeeper, "SetState", KeeperController.State.Recovery, 60f);
        Invoke(UnityEngine.Object.FindAnyObjectByType<ArenaGate>(), "SetRaised", true, false);
        Invoke(testKeeper, "SpawnShockwave");
        var left = Field<GroundShockwave>(testKeeper, "leftShockwave");
        var right = Field<GroundShockwave>(testKeeper, "rightShockwave");
        Check(left != null && right != null, "Two waves spawned");
        leftStart = left.transform.position.x;
        rightStart = right.transform.position.x;
        testStep = 0;
        testStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }
    static void Check(bool value, string description)
    {
        if (!value) throw new Exception("PRESENTATION TEST FAILED: " + description);
        Debug.Log("PRESENTATION TEST PASS: " + description);
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || testKeeper == null) { EditorApplication.update -= Tick; return; }
        double elapsed = EditorApplication.timeSinceStartup - testStart;
        try
        {
            if (testStep == 0 && elapsed > 0.22)
            {
                var left = Field<GroundShockwave>(testKeeper, "leftShockwave");
                var right = Field<GroundShockwave>(testKeeper, "rightShockwave");
                Check(left != null && left.transform.position.x < leftStart, "Left wave travels left");
                Check(right != null && right.transform.position.x > rightStart, "Right wave travels right");
                Check(left.GetComponent<SpriteRenderer>().flipX && !right.GetComponent<SpriteRenderer>().flipX, "Opposite sprite facing");
                Capture(3, "waves");
                testKeeper.GetComponent<BossHealth>().TakeDamage(1);
                testPlayer.TakeDamage(1, testPlayer.transform.position + Vector3.left);
                testStep++;
            }
            if (testStep == 1 && elapsed > 0.5)
            {
                var bossHud = UnityEngine.Object.FindAnyObjectByType<BossHealthUI>();
                var fill = Field<Image>(bossHud, "healthFill");
                Check(fill.fillAmount < 1f && fill.fillAmount >= 0.79f, "Animated boss health drain");
                var playerHud = UnityEngine.Object.FindAnyObjectByType<PlayerHealthUI>();
                Check(Field<int>(playerHud, "previousHealth") == testPlayer.CurrentHealth, "Player HUD tracks damage");
                Check(Field<float>(playerHud, "hitPulse") > 0f, "Player damage pulse active");
                Check(UnityEngine.Object.FindAnyObjectByType<ArenaGate>().IsRaised, "Gate raises");
                ScreenCapture.CaptureScreenshot("/private/tmp/thereddoor-hud-damage.png");
                testKeeper.GetComponent<BossHealth>().TakeDamage(999);
                testStep++;
            }
            if (testStep == 2 && elapsed > 1.8)
            {
                Check(Field<GroundShockwave>(testKeeper, "leftShockwave") == null &&
                    Field<GroundShockwave>(testKeeper, "rightShockwave") == null, "Both waves cleared on defeat");
                var sr = Field<SpriteRenderer>(testKeeper, "spriteRenderer");
                var box = testKeeper.GetComponent<BoxCollider2D>();
                float floor = box.transform.TransformPoint(box.offset + Vector2.down * box.size.y * .5f).y;
                Check(Mathf.Abs(sr.bounds.min.y - floor) < .01f, "Death bottom anchored to floor with disabled collider");
                Check(!box.enabled, "Defeated boss no longer blocks player");
                var gate = UnityEngine.Object.FindAnyObjectByType<ArenaGate>();
                Check(!gate.IsRaised && !gate.GetComponent<Collider2D>().enabled, "Gate opens on defeat");
                Check(sr.color == Color.white, "Keeper retains natural sprite color");
                Capture(3, "death");
                ScreenCapture.CaptureScreenshot("/private/tmp/thereddoor-hud-death.png");
                Debug.Log("PRESENTATION TESTS COMPLETE. Stop Play to discard test-only damage/positions.");
                EditorApplication.update -= Tick;
            }
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.update -= Tick; }
    }

    [MenuItem("Tools/TheRedDoor/Run Wave Collision Checks")]
    static void CollisionTests()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter fresh Play first.");
        var keeper = UnityEngine.Object.FindAnyObjectByType<KeeperController>();
        var player = Field<PlayerHealth>(keeper, "target");
        var sr = Field<SpriteRenderer>(keeper, "spriteRenderer");
        foreach (KeeperController.State state in Enum.GetValues(typeof(KeeperController.State)))
        {
            Invoke(keeper, "SetState", state, 60f);
            Check(sr.color == Color.white, "No debug tint in " + state);
        }
        Invoke(keeper, "SetState", KeeperController.State.Recovery, 60f);
        var rb = player.GetComponent<Rigidbody2D>();
        rb.constraints = RigidbodyConstraints2D.FreezeAll;
        foreach (int direction in new[] { -1, 1 })
        {
            player.transform.position = new Vector3(keeper.transform.position.x + direction * 2.5f, -1.7f, 0);
            rb.linearVelocity = Vector2.zero;
            typeof(PlayerHealth).GetField("invulnerableUntil", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, 0f);
            Physics2D.SyncTransforms();
            int before = player.CurrentHealth;
            Invoke(keeper, "SpawnShockwave");
            var wave = Field<GroundShockwave>(keeper, direction < 0 ? "leftShockwave" : "rightShockwave");
            var other = Field<GroundShockwave>(keeper, direction < 0 ? "rightShockwave" : "leftShockwave");
            for (int i = 0; i < 80 && wave.IsTravelling; i++) Invoke(wave, "FixedUpdate");
            Check(player.CurrentHealth == before - 1, (direction < 0 ? "Left" : "Right") + " wave deals exactly 1 HP");
            Check(!wave.IsTravelling && other.IsTravelling, "Hit consumes only the contacting wave");
            Invoke(keeper, "ClearShockwave");
        }
        Invoke(keeper, "SpawnShockwave");
        player.Kill(player.transform.position);
        Check(Field<GroundShockwave>(keeper, "leftShockwave") == null && Field<GroundShockwave>(keeper, "rightShockwave") == null,
            "Player death cancels both waves");
        var rootPosition = keeper.transform.position;
        keeper.GetComponent<BossHealth>().TakeDamage(999);
        var animator = keeper.GetComponentInChildren<Animator>();
        var driver = keeper.GetComponent<KeeperAnimationController>();
        Invoke(driver, "LateUpdate");
        float floorY = keeper.GetComponent<BoxCollider2D>().transform.TransformPoint(new Vector3(0, -1, 0)).y;
        for (int frame = 0; frame < 18; frame++)
        {
            animator.Play("Base Layer.Keeper_Death", 0, frame / 18f);
            animator.Update(0f);
            Invoke(driver, "LateUpdate");
            Check(Mathf.Abs(sr.bounds.min.y - floorY) < .01f, "Death frame " + (frame + 1) + " grounded");
        }
        Check(keeper.transform.position == rootPosition, "Death anchoring never moves boss root");
        Debug.Log("WAVE COLLISION AND DEATH FRAME CHECKS COMPLETE. Stop Play without saving.");
        EditorApplication.isPlaying = false;
    }
}
