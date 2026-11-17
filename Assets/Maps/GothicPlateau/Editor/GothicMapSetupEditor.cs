using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GothicPlateau.Editor
{
    public static class GothicMapSetupEditor
    {
        private const string MapFolder = "Assets/Maps/GothicPlateau";
        private const string SceneGlbPath = MapFolder + "/scene_final.glb";
        private const string FlowerGlbPath = MapFolder + "/flower.glb";
        private const string CliffWallGlbPath = MapFolder + "/Invisible_Cliff_Wall.glb";
        private const string GreatTreeBarrierGlbPath = MapFolder + "/Invisible_GreatTree_Barrier.glb";
        private const string FlowersJsonPath = MapFolder + "/Data/flowers.json";
        private const string LandmarksJsonPath = MapFolder + "/Data/landmarks.json";
        private const string SkyboxMatPath = MapFolder + "/Materials/GothicNightSkybox.mat";

        private const string MapRootName = "GothicPlateau_Map";

        [Serializable]
        private class LandmarkLightEntry
        {
            public string name;
            public float[] position;
            public string color;
            public float intensity;
            public float distance;
        }

        [Serializable]
        private class LandmarksFile
        {
            public LandmarkLightEntry[] lights;
        }

        [MenuItem("Tools/Gothic Plateau/Setup Full Map In Active Scene")]
        public static void SetupMenu()
        {
            string report = SetupActiveScene();
            Debug.Log("[GothicMapSetup]\n" + report);
        }

        public static string SetupActiveScene()
        {
            var sb = new StringBuilder();
            GameObject sceneGlb = AssetDatabase.LoadAssetAtPath<GameObject>(SceneGlbPath);
            GameObject flowerGlb = AssetDatabase.LoadAssetAtPath<GameObject>(FlowerGlbPath);
            GameObject cliffGlb = AssetDatabase.LoadAssetAtPath<GameObject>(CliffWallGlbPath);
            GameObject greatTreeBarrierGlb = AssetDatabase.LoadAssetAtPath<GameObject>(GreatTreeBarrierGlbPath);
            TextAsset flowersJson = AssetDatabase.LoadAssetAtPath<TextAsset>(FlowersJsonPath);
            TextAsset landmarksJson = AssetDatabase.LoadAssetAtPath<TextAsset>(LandmarksJsonPath);

            if (sceneGlb == null || flowerGlb == null || cliffGlb == null || flowersJson == null)
            {
                throw new InvalidOperationException(
                    $"Missing required GothicPlateau assets under '{MapFolder}': " +
                    $"scene={sceneGlb != null}, flower={flowerGlb != null}, cliff={cliffGlb != null}, flowersJson={flowersJson != null}");
            }

            // 1. Xoa ban cu neu da chay truoc do (idempotent)
            GameObject existing = GameObject.Find("/" + MapRootName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            GameObject mapRoot = new GameObject(MapRootName);
            Undo.RegisterCreatedObjectUndo(mapRoot, "Create GothicPlateau_Map");
            mapRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // 2. Dat scene_final.glb thanh con MapGeometry
            GameObject mapGeom = (GameObject)PrefabUtility.InstantiatePrefab(sceneGlb);
            mapGeom.name = "MapGeometry";
            mapGeom.transform.SetParent(mapRoot.transform, false);
            mapGeom.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // 3. Gan Collider chon loc (bo qua Pillars xa ngoai vuc va tan la cay mảnh; cay mảnh dung CapsuleCollider goc than)
            GameObject treeCollidersRoot = new GameObject("TreeTrunkColliders");
            treeCollidersRoot.transform.SetParent(mapRoot.transform, false);

            int meshColliderCount = 0;
            int treeColliderCount = 0;
            int skippedPillars = 0;
            MeshCollider terrainCollider = null;

            MeshFilter[] mfs = mapGeom.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mfs.Length; i++)
            {
                MeshFilter mf = mfs[i];
                if (mf.sharedMesh == null) continue;
                string relPath = AnimationUtility.CalculateTransformPath(mf.transform, mapGeom.transform);

                if (relPath.StartsWith("Pillars/", StringComparison.OrdinalIgnoreCase))
                {
                    skippedPillars++;
                    continue;
                }

                if (relPath.StartsWith("Trees/Slender_Tree_", StringComparison.OrdinalIgnoreCase))
                {
                    CreateSlenderTreeTrunkCollider(mf, treeCollidersRoot.transform);
                    treeColliderCount++;
                    continue;
                }

                MeshCollider mc = mf.gameObject.GetComponent<MeshCollider>();
                if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                meshColliderCount++;

                if (mf.name == "Plateau_Terrain")
                {
                    terrainCollider = mc;
                }
            }
            sb.AppendLine($"Colliders: {meshColliderCount} MeshColliders, {treeColliderCount} SlenderTree CapsuleColliders, skipped {skippedPillars} distant Mist_Pillars");

            // 4. Gan tuong tang hinh Invisible_Cliff_Wall doc mep vuc & Invisible_GreatTree_Barrier quanh re Dai Thu o Boss
            int cliffColliderCount = InstantiateInvisibleBarrier(cliffGlb, "Invisible_Cliff_Wall", mapRoot.transform);
            sb.AppendLine($"Invisible cliff wall colliders: {cliffColliderCount} (MeshRenderer disabled)");
            if (greatTreeBarrierGlb != null)
            {
                int treeWallCount = InstantiateInvisibleBarrier(greatTreeBarrierGlb, "Invisible_GreatTree_Barrier", mapRoot.transform);
                sb.AppendLine($"Invisible Great_Tree root barrier colliders: {treeWallCount} (MeshRenderer disabled)");
            }

            // 5. Rai 11,100 cum hoa bang GPU Instancing
            GameObject flowersGo = new GameObject("GothicFlowers");
            flowersGo.transform.SetParent(mapRoot.transform, false);
            flowersGo.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            GothicFlowerInstancer flowerInstancer = flowersGo.AddComponent<GothicFlowerInstancer>();
            ConfigureFlowerInstancerSerialized(flowerInstancer, flowerGlb, flowersJson);
            flowerInstancer.Rebuild();
            sb.AppendLine($"GothicFlowerInstancer rebuilt: {flowerInstancer.InstanceCount} instances");

            // 6. Tao den PointLight am tu landmarks.json & anh sang / suong mu Gothic ban dem
            int lightsCreated = BuildLightsFromLandmarks(mapRoot.transform, landmarksJson);
            ConfigureAtmosphereAndDirectionalLight(sb);
            sb.AppendLine($"Point lights created from landmarks.json: {lightsCreated}");

            // 7. Tat Plane vuong cu
            GameObject oldPlane = GameObject.Find("/Plane");
            if (oldPlane != null)
            {
                Undo.RecordObject(oldPlane, "Disable old flat Plane");
                oldPlane.SetActive(false);
                sb.AppendLine("Disabled legacy '/Plane' (kept inactive for safety).");
            }

            // 8. Tim bai co rong, bang phang nhat trong khu vuc Boss de dat Knight, Boss va Main Camera
            Physics.SyncTransforms();
            PlaceCharactersInBossFlowerMeadow(terrainCollider, mapRoot.transform, sb);

            // Noi tham chieu BattleLoopManager -> bossAttackController neu dang null
            WireBattleManagerReferences();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            sb.AppendLine("Saved active SampleScene.");
            return sb.ToString();
        }

        private static int InstantiateInvisibleBarrier(GameObject barrierGlb, string goName, Transform parent)
        {
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(barrierGlb);
            go.name = goName;
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            int colCount = 0;
            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mfs.Length; i++)
            {
                MeshFilter mf = mfs[i];
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.enabled = false;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (mf.sharedMesh != null)
                {
                    MeshCollider mc = mf.gameObject.GetComponent<MeshCollider>();
                    if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                    colCount++;
                }
            }
            return colCount;
        }

        private static void ConfigureFlowerInstancerSerialized(GothicFlowerInstancer instancer, GameObject flowerGlb, TextAsset flowersJson)
        {
            SerializedObject so = new SerializedObject(instancer);
            so.FindProperty("flowerPrefab").objectReferenceValue = flowerGlb;
            so.FindProperty("flowersJson").objectReferenceValue = flowersJson;
            so.FindProperty("mirrorX").boolValue = true;
            so.FindProperty("scaleMultiplier").floatValue = 1f;
            so.FindProperty("heightOffset").floatValue = 0.01f;
            so.FindProperty("castShadows").boolValue = false;
            so.FindProperty("receiveShadows").boolValue = true;
            so.FindProperty("drawInEditMode").boolValue = true;
            so.FindProperty("drawInPlayMode").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateSlenderTreeTrunkCollider(MeshFilter mf, Transform parent)
        {
            Mesh mesh = mf.sharedMesh;
            Vector3[] verts = mesh.vertices;
            if (verts == null || verts.Length == 0) return;

            Transform tr = mf.transform;
            float minY = float.PositiveInfinity;
            for (int i = 0; i < verts.Length; i++)
            {
                float wy = tr.TransformPoint(verts[i]).y;
                if (wy < minY) minY = wy;
            }

            Vector3 sum = Vector3.zero;
            int cnt = 0;
            float cutoffY = minY + 1.5f;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 wp = tr.TransformPoint(verts[i]);
                if (wp.y <= cutoffY)
                {
                    sum += wp;
                    cnt++;
                }
            }

            Vector3 rootPos = cnt > 0 ? sum / cnt : tr.TransformPoint(mesh.bounds.center);
            rootPos.y = minY;

            GameObject go = new GameObject("Collider_" + mf.name);
            go.transform.SetParent(parent, false);
            go.transform.position = rootPos;
            CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
            cap.radius = 0.38f;
            cap.height = 5.0f;
            cap.center = new Vector3(0f, 2.3f, 0f);
        }

        private static int BuildLightsFromLandmarks(Transform mapRoot, TextAsset landmarksJson)
        {
            if (landmarksJson == null) return 0;
            LandmarksFile data = JsonUtility.FromJson<LandmarksFile>(landmarksJson.text);
            if (data == null || data.lights == null) return 0;

            GameObject lightsParent = new GameObject("GothicPlateau_Lights");
            lightsParent.transform.SetParent(mapRoot, false);

            int count = 0;
            for (int i = 0; i < data.lights.Length; i++)
            {
                LandmarkLightEntry entry = data.lights[i];
                if (entry == null || entry.position == null || entry.position.Length < 3) continue;

                // Mirror X khop glTFast
                Vector3 pos = new Vector3(-entry.position[0], entry.position[1], entry.position[2]);
                GameObject lgo = new GameObject(string.IsNullOrEmpty(entry.name) ? $"WarmLight_{i}" : entry.name);
                lgo.transform.SetParent(lightsParent.transform, false);
                lgo.transform.position = pos;

                Light l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                Color col = new Color(1f, 0.55f, 0.17f, 1f);
                if (!string.IsNullOrEmpty(entry.color))
                {
                    ColorUtility.TryParseHtmlString(entry.color, out col);
                }
                l.color = col;
                l.intensity = Mathf.Max(2.5f, entry.intensity * 3.8f);
                l.range = Mathf.Max(10f, entry.distance * 0.85f);
                l.shadows = LightShadows.None;
                count++;
            }
            return count;
        }

        private static void ConfigureAtmosphereAndDirectionalLight(StringBuilder sb)
        {
            GameObject dirGo = GameObject.Find("/Directional Light");
            if (dirGo != null)
            {
                Undo.RecordObject(dirGo.transform, "Adjust Moonlight Direction");
                dirGo.transform.rotation = Quaternion.Euler(46f, -38f, 0f);
                Light dl = dirGo.GetComponent<Light>();
                if (dl != null)
                {
                    Undo.RecordObject(dl, "Adjust Moonlight Color & Intensity");
                    dl.useColorTemperature = false;
                    dl.color = new Color(0.68f, 0.77f, 0.96f, 1f);
                    dl.intensity = 1.35f;
                    dl.shadows = LightShadows.Soft;
                }
            }

            // Tao hoac cap nhat Skybox dem Gothic de khong bi nen troi xanh ban ngay
            string matDir = MapFolder + "/Materials";
            if (!AssetDatabase.IsValidFolder(matDir))
            {
                AssetDatabase.CreateFolder(MapFolder, "Materials");
            }
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                Material skyMat = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMatPath);
                if (skyMat == null)
                {
                    skyMat = new Material(skyShader);
                    AssetDatabase.CreateAsset(skyMat, SkyboxMatPath);
                }
                skyMat.SetFloat("_SunSize", 0.03f);
                skyMat.SetFloat("_AtmosphereThickness", 0.45f);
                skyMat.SetColor("_SkyTint", new Color(0.05f, 0.045f, 0.09f, 1f));
                skyMat.SetColor("_GroundColor", new Color(0.03f, 0.025f, 0.04f, 1f));
                skyMat.SetFloat("_Exposure", 0.55f);
                EditorUtility.SetDirty(skyMat);
                RenderSettings.skybox = skyMat;
            }

            if (dirGo != null)
            {
                Light dl = dirGo.GetComponent<Light>();
                if (dl != null)
                {
                    dl.intensity = 1.05f;
                }
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.12f, 0.15f, 0.24f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.10f, 0.16f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.045f, 0.055f, 0.095f, 1f);
            RenderSettings.fogDensity = 0.008f;

            sb.AppendLine("Configured Moonlight, GothicNightSkybox & night atmospheric fog.");
        }

        private static bool RaycastTerrainOnly(MeshCollider terrainCol, Vector3 posXZ, out RaycastHit hit)
        {
            hit = default;
            if (terrainCol == null) return false;
            Ray r = new Ray(new Vector3(posXZ.x, 30f, posXZ.z), Vector3.down);
            return terrainCol.Raycast(r, out hit, 45f);
        }

        private static float MeasureHorizontalClearance(MeshCollider terrainCol, Collider[] playerBossCols, Vector3 pos)
        {
            // Quet theo hinh tru ngang tam nguoi (0.55m .. 2.0m) de khong tinh nham tan cay tren cao
            Vector3 p0 = pos + Vector3.up * 0.55f;
            Vector3 p1 = pos + Vector3.up * 2.00f;
            float[] radii = { 0.8f, 1.4f, 2.0f, 2.6f, 3.2f, 3.8f, 4.4f, 5.0f, 5.6f, 6.2f };
            for (int i = 0; i < radii.Length; i++)
            {
                Collider[] cols = Physics.OverlapCapsule(p0, p1, radii[i], ~0, QueryTriggerInteraction.Ignore);
                for (int c = 0; c < cols.Length; c++)
                {
                    Collider col = cols[c];
                    if (col == null || col == terrainCol) continue;
                    bool isChar = false;
                    for (int k = 0; k < playerBossCols.Length; k++)
                    {
                        if (col == playerBossCols[k]) { isChar = true; break; }
                    }
                    if (isChar) continue;
                    return Mathf.Max(0f, radii[i] - 0.6f);
                }
            }
            return 6.5f;
        }

        private static void PlaceCharactersInBossFlowerMeadow(MeshCollider terrainCol, Transform mapRoot, StringBuilder sb)
        {
            GameObject knight = GameObject.Find("/Knight");
            GameObject bossRoot = GameObject.Find("/Boss");
            GameObject bossBody = GameObject.Find("/Boss/Boss_Body");
            GameObject mainCam = GameObject.Find("/Main Camera");

            if (knight == null || bossBody == null || terrainCol == null)
            {
                sb.AppendLine("WARNING: Could not find Knight, Boss_Body, or Plateau_Terrain collider for auto-placement.");
                return;
            }

            List<Collider> charCols = new List<Collider>();
            charCols.AddRange(knight.GetComponentsInChildren<Collider>(true));
            if (bossRoot != null) charCols.AddRange(bossRoot.GetComponentsInChildren<Collider>(true));
            Collider[] charColArr = charCols.ToArray();

            // Quet bai hoa Boss rong nhat (bao gom khu vuc Z=22..31 vua don cay & den)
            Vector3 bestKnight = new Vector3(-13.0f, 0.78f, 24.0f);
            Vector3 bestBoss = new Vector3(-11.5f, 0.56f, 29.5f);
            float bestScore = -1f;

            Vector3 camOffset = new Vector3(0f, 6f, -7.09f); // Giu dung offset camera cu cua game

            for (float x = -28f; x <= -7f; x += 1.0f)
            {
                for (float z = 0f; z <= 31f; z += 1.0f)
                {
                    if (!RaycastTerrainOnly(terrainCol, new Vector3(x, 0f, z), out RaycastHit kHit)) continue;
                    if (kHit.normal.y < 0.94f) continue;
                    if (kHit.point.y < 0.3f || kHit.point.y > 3.6f) continue;

                    float kClear = MeasureHorizontalClearance(terrainCol, charColArr, kHit.point);
                    if (kClear < 2.6f) continue;

                    // Camera nhin theo +Z (euler 40,0,0) nen dat Boss o phia +Z truoc mat Knight khoang 5.5m - 6.5m
                    float[] dzCandidates = { 5.5f, 6.5f };
                    float[] dxCandidates = { 0f, -2.0f, 2.0f };

                    for (int a = 0; a < dzCandidates.Length; a++)
                    {
                        for (int b = 0; b < dxCandidates.Length; b++)
                        {
                            float bx = x + dxCandidates[b];
                            float bz = z + dzCandidates[a];
                            if (bx < -29f || bx > -6.5f || bz < 0f || bz > 31.5f) continue;

                            if (!RaycastTerrainOnly(terrainCol, new Vector3(bx, 0f, bz), out RaycastHit bHit)) continue;
                            if (bHit.normal.y < 0.94f) continue;
                            if (Mathf.Abs(bHit.point.y - kHit.point.y) > 0.50f) continue;

                            float bClear = MeasureHorizontalClearance(terrainCol, charColArr, bHit.point);
                            if (bClear < 2.8f) continue;

                            Vector3 mid = (kHit.point + bHit.point) * 0.5f;
                            if (!RaycastTerrainOnly(terrainCol, mid, out RaycastHit mHit)) continue;
                            float mClear = MeasureHorizontalClearance(terrainCol, charColArr, mHit.point);
                            if (mClear < 2.8f) continue;

                            Vector3 testCamPos = kHit.point + camOffset;
                            bool camBlocked = Physics.Linecast(testCamPos, kHit.point + Vector3.up * 1.2f, out RaycastHit camHit, ~0, QueryTriggerInteraction.Ignore)
                                && camHit.collider != terrainCol;
                            bool bossCamBlocked = Physics.Linecast(testCamPos, bHit.point + Vector3.up * 1.2f, out RaycastHit bossCamHit, ~0, QueryTriggerInteraction.Ignore)
                                && bossCamHit.collider != terrainCol;
                            if (camBlocked || bossCamBlocked) continue;

                            float flatnessBonus = (kHit.normal.y + bHit.normal.y) * 5f - Mathf.Abs(kHit.point.y - bHit.point.y) * 5f;
                            float clearScore = Mathf.Min(kClear, 5.5f) + Mathf.Min(bClear, 5.5f) + Mathf.Min(mClear, 5.5f);
                            // Uu tien khoang san rong thoang Z in [22..30]
                            float meadowCenterBonus = -0.05f * (Mathf.Abs(x + 12.5f) + Mathf.Abs(z - 23.5f));

                            float score = clearScore + flatnessBonus + meadowCenterBonus;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestKnight = kHit.point;
                                bestBoss = bHit.point;
                            }
                        }
                    }
                }
            }

            Undo.RecordObject(knight.transform, "Place Knight in Boss Meadow");
            CharacterController cc = knight.GetComponent<CharacterController>();
            bool ccWasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            knight.transform.position = bestKnight + Vector3.up * 0.15f;
            Vector3 lookToBoss = Vector3.ProjectOnPlane(bestBoss - bestKnight, Vector3.up);
            if (lookToBoss.sqrMagnitude > 0.001f)
            {
                knight.transform.rotation = Quaternion.LookRotation(lookToBoss.normalized, Vector3.up);
            }
            if (cc != null) cc.enabled = ccWasEnabled;

            if (bossRoot != null)
            {
                Undo.RecordObject(bossRoot.transform, "Place Boss Root in Boss Meadow");
                Undo.RecordObject(bossBody.transform, "Reset Boss Body Local Pos");
                bossRoot.transform.position = bestBoss;
                bossRoot.transform.rotation = Quaternion.identity;
                bossBody.transform.localPosition = Vector3.zero;
                Vector3 lookToKnight = Vector3.ProjectOnPlane(bestKnight - bestBoss, Vector3.up);
                if (lookToKnight.sqrMagnitude > 0.001f)
                {
                    bossBody.transform.rotation = Quaternion.LookRotation(lookToKnight.normalized, Vector3.up);
                }
            }

            if (mainCam != null)
            {
                Undo.RecordObject(mainCam.transform, "Align Main Camera to Knight in Boss Meadow");
                mainCam.transform.position = knight.transform.position + camOffset;
                if (SceneView.lastActiveSceneView != null)
                {
                    Vector3 arenaMid = (bestKnight + bestBoss) * 0.5f + Vector3.up * 1.2f;
                    SceneView.lastActiveSceneView.LookAt(arenaMid, Quaternion.Euler(35f, 15f, 0f), 14f);
                }
            }

            float kClearFinal = MeasureHorizontalClearance(terrainCol, charColArr, bestKnight);
            float bClearFinal = MeasureHorizontalClearance(terrainCol, charColArr, bestBoss);
            sb.AppendLine($"Placed Knight at {knight.transform.position:F2} (clearance ~{kClearFinal:F1}m)");
            sb.AppendLine($"Placed Boss/Boss_Body at {bossBody.transform.position:F2} (clearance ~{bClearFinal:F1}m)");
            if (mainCam != null)
            {
                sb.AppendLine($"Placed Main Camera at {mainCam.transform.position:F2}");
            }
        }

        private static void WireBattleManagerReferences()
        {
            BattleLoopManager bm = UnityEngine.Object.FindFirstObjectByType<BattleLoopManager>();
            BossAttackController bac = UnityEngine.Object.FindFirstObjectByType<BossAttackController>();
            if (bm != null && bac != null)
            {
                SerializedObject so = new SerializedObject(bm);
                SerializedProperty prop = so.FindProperty("bossAttackController");
                if (prop != null && prop.objectReferenceValue == null)
                {
                    prop.objectReferenceValue = bac;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
