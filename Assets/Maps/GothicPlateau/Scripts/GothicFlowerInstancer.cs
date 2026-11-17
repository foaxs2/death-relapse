using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GothicPlateau
{
    /// <summary>
    /// Vẽ 11,100 cụm hoa từ flowers.json bằng GPU Instancing (Graphics.DrawMeshInstanced)
    /// hoạt động trong cả Scene View (Edit Mode) và Play Mode.
    /// </summary>
    [ExecuteAlways]
    public class GothicFlowerInstancer : MonoBehaviour
    {
        [Header("Flower Assets")]
        [SerializeField] private GameObject flowerPrefab;
        [SerializeField] private TextAsset flowersJson;

        [Header("Transform & Alignment")]
        [Tooltip("glTFast lật trục X khi import từ glTF tay phải sang Unity tay trái -> bật true để khớp 100% với scene_final.glb.")]
        [SerializeField] private bool mirrorX = true;
        [SerializeField] private float scaleMultiplier = 1f;
        [SerializeField] private float heightOffset = 0.01f;

        [Header("Rendering")]
        [SerializeField] private bool castShadows = false;
        [SerializeField] private bool receiveShadows = true;
        [SerializeField] private bool drawInEditMode = true;
        [SerializeField] private bool drawInPlayMode = true;
        [SerializeField] private int renderLayer = 0;

        [Serializable]
        private class FlowerEntry
        {
            public float[] position;
            public float[] rotation;
            public float[] scale;
            public string zone;
        }

        [Serializable]
        private class FlowerFile
        {
            public string coordinateSystem;
            public string prototype;
            public int count;
            public FlowerEntry[] flowers;
        }

        private class SubMeshRenderData
        {
            public Mesh mesh;
            public Material[] materials;
            public Matrix4x4 localOffset;
        }

        private readonly List<SubMeshRenderData> _submeshes = new List<SubMeshRenderData>();
        private readonly List<Matrix4x4[]> _localBatches = new List<Matrix4x4[]>();
        private readonly List<Matrix4x4[]> _worldBatches = new List<Matrix4x4[]>();
        private readonly List<Material> _clonedMaterials = new List<Material>();
        private Matrix4x4 _lastParentMatrix = Matrix4x4.zero;
        private bool _isBuilt = false;

        public int InstanceCount { get; private set; }

        private void OnEnable()
        {
            Rebuild();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            CleanupClonedMaterials();
            _isBuilt = false;
        }

        private void OnValidate()
        {
            _isBuilt = false;
        }

        [ContextMenu("Rebuild Flower Instances")]
        public void Rebuild()
        {
            CleanupClonedMaterials();
            _submeshes.Clear();
            _localBatches.Clear();
            _worldBatches.Clear();
            _lastParentMatrix = Matrix4x4.zero;
            InstanceCount = 0;
            _isBuilt = false;

            if (flowerPrefab == null || flowersJson == null)
            {
                return;
            }

            ExtractFlowerSubmeshes();
            if (_submeshes.Count == 0)
            {
                return;
            }

            FlowerFile data = JsonUtility.FromJson<FlowerFile>(flowersJson.text);
            if (data == null || data.flowers == null || data.flowers.Length == 0)
            {
                return;
            }

            const int BatchLimit = 1023;
            List<Matrix4x4> allMatrices = new List<Matrix4x4>(data.flowers.Length);

            for (int i = 0; i < data.flowers.Length; i++)
            {
                FlowerEntry entry = data.flowers[i];
                if (entry == null || entry.position == null || entry.position.Length < 3)
                {
                    continue;
                }

                float px = entry.position[0];
                float py = entry.position[1] + heightOffset;
                float pz = entry.position[2];

                float rx = (entry.rotation != null && entry.rotation.Length >= 3) ? entry.rotation[0] : 0f;
                float ry = (entry.rotation != null && entry.rotation.Length >= 3) ? entry.rotation[1] : 0f;
                float rz = (entry.rotation != null && entry.rotation.Length >= 3) ? entry.rotation[2] : 0f;

                float sx = (entry.scale != null && entry.scale.Length >= 3) ? entry.scale[0] : 1f;
                float sy = (entry.scale != null && entry.scale.Length >= 3) ? entry.scale[1] : 1f;
                float sz = (entry.scale != null && entry.scale.Length >= 3) ? entry.scale[2] : 1f;

                Vector3 pos = mirrorX ? new Vector3(-px, py, pz) : new Vector3(px, py, pz);
                Quaternion rot = BuildFlowerRotation(rx, ry, rz, mirrorX);
                Vector3 scl = new Vector3(sx * scaleMultiplier, sy * scaleMultiplier, sz * scaleMultiplier);

                allMatrices.Add(Matrix4x4.TRS(pos, rot, scl));
            }

            InstanceCount = allMatrices.Count;
            for (int offset = 0; offset < InstanceCount; offset += BatchLimit)
            {
                int count = Mathf.Min(BatchLimit, InstanceCount - offset);
                Matrix4x4[] chunk = new Matrix4x4[count];
                allMatrices.CopyTo(offset, chunk, 0, count);
                _localBatches.Add(chunk);
                _worldBatches.Add(new Matrix4x4[count]);
            }

            SyncWorldBatches(true);
            _isBuilt = true;
        }

        private static Quaternion BuildFlowerRotation(float rxRad, float ryRad, float rzRad, bool reflectX)
        {
            float dx = rxRad * Mathf.Rad2Deg;
            float dy = ryRad * Mathf.Rad2Deg;
            float dz = rzRad * Mathf.Rad2Deg;

            // Blender to_euler("XYZ") = extrinsic X -> Y -> Z => Rz(dz) * Ry(dy) * Rx(dx).
            // Dưới phép phản chiếu trục X của glTFast: góc quanh Y và Z đổi dấu, quanh X giữ nguyên.
            if (reflectX)
            {
                return Quaternion.AngleAxis(-dz, Vector3.forward)
                     * Quaternion.AngleAxis(-dy, Vector3.up)
                     * Quaternion.AngleAxis(dx, Vector3.right);
            }

            return Quaternion.AngleAxis(dz, Vector3.forward)
                 * Quaternion.AngleAxis(dy, Vector3.up)
                 * Quaternion.AngleAxis(dx, Vector3.right);
        }

        private void ExtractFlowerSubmeshes()
        {
            MeshFilter[] mfs = flowerPrefab.GetComponentsInChildren<MeshFilter>(true);
            Matrix4x4 rootInv = flowerPrefab.transform.worldToLocalMatrix;

            for (int i = 0; i < mfs.Length; i++)
            {
                MeshFilter mf = mfs[i];
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || mr.sharedMaterials == null)
                {
                    continue;
                }

                Material[] srcMats = mr.sharedMaterials;
                Material[] instMats = new Material[srcMats.Length];
                for (int m = 0; m < srcMats.Length; m++)
                {
                    if (srcMats[m] == null) continue;
                    Material clone = new Material(srcMats[m])
                    {
                        name = srcMats[m].name + "_Instanced",
                        hideFlags = HideFlags.DontSave,
                        enableInstancing = true
                    };
                    instMats[m] = clone;
                    _clonedMaterials.Add(clone);
                }

                _submeshes.Add(new SubMeshRenderData
                {
                    mesh = mf.sharedMesh,
                    materials = instMats,
                    localOffset = rootInv * mf.transform.localToWorldMatrix
                });
            }
        }

        private void CleanupClonedMaterials()
        {
            for (int i = 0; i < _clonedMaterials.Count; i++)
            {
                if (_clonedMaterials[i] != null)
                {
                    if (Application.isPlaying)
                        Destroy(_clonedMaterials[i]);
                    else
                        DestroyImmediate(_clonedMaterials[i]);
                }
            }
            _clonedMaterials.Clear();
        }

        private void SyncWorldBatches(bool force)
        {
            Matrix4x4 parentMat = transform.localToWorldMatrix;
            if (!force && parentMat == _lastParentMatrix)
            {
                return;
            }
            _lastParentMatrix = parentMat;

            bool isIdentity = (parentMat == Matrix4x4.identity);
            for (int b = 0; b < _localBatches.Count; b++)
            {
                Matrix4x4[] src = _localBatches[b];
                Matrix4x4[] dst = _worldBatches[b];
                if (isIdentity)
                {
                    Array.Copy(src, dst, src.Length);
                }
                else
                {
                    for (int i = 0; i < src.Length; i++)
                    {
                        dst[i] = parentMat * src[i];
                    }
                }
            }
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (Application.isPlaying || !drawInEditMode) return;
            DrawInstances(cam);
        }

        private void Update()
        {
            if (!Application.isPlaying || !drawInPlayMode) return;
            DrawInstances(null);
        }

        private void DrawInstances(Camera targetCamera)
        {
            if (!_isBuilt)
            {
                Rebuild();
                if (!_isBuilt) return;
            }

            SyncWorldBatches(false);

            ShadowCastingMode shadowMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

            for (int s = 0; s < _submeshes.Count; s++)
            {
                SubMeshRenderData sub = _submeshes[s];
                int subMeshCount = Mathf.Min(sub.mesh.subMeshCount, sub.materials.Length);
                bool hasLocalOffset = (sub.localOffset != Matrix4x4.identity);

                for (int subIdx = 0; subIdx < subMeshCount; subIdx++)
                {
                    Material mat = sub.materials[subIdx];
                    if (mat == null) continue;

                    for (int b = 0; b < _worldBatches.Count; b++)
                    {
                        Matrix4x4[] matrices = _worldBatches[b];
                        if (hasLocalOffset)
                        {
                            // Hầu hết flower.glb offset = Identity nên nhánh này gần như không chạy
                            Matrix4x4[] adjusted = new Matrix4x4[matrices.Length];
                            for (int i = 0; i < matrices.Length; i++)
                            {
                                adjusted[i] = matrices[i] * sub.localOffset;
                            }
                            Graphics.DrawMeshInstanced(
                                sub.mesh,
                                subIdx,
                                mat,
                                adjusted,
                                adjusted.Length,
                                null,
                                shadowMode,
                                receiveShadows,
                                renderLayer,
                                targetCamera
                            );
                        }
                        else
                        {
                            Graphics.DrawMeshInstanced(
                                sub.mesh,
                                subIdx,
                                mat,
                                matrices,
                                matrices.Length,
                                null,
                                shadowMode,
                                receiveShadows,
                                renderLayer,
                                targetCamera
                            );
                        }
                    }
                }
            }
        }
    }
}
