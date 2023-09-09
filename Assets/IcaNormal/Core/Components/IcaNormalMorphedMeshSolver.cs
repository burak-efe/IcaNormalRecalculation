using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Serialization;

namespace Ica.Normal
{
    /// <summary>
    /// The main Component of the package
    /// </summary>
    public class IcaNormalMorphedMeshSolver : MonoBehaviour
    {
        public enum NormalOutputEnum
        {
            WriteToMesh,
            WriteToMaterial
        }

        public NormalOutputEnum NormalOutputTarget = NormalOutputEnum.WriteToMesh;
        [Range(0,180)]
        public float Angle = 180f;
        public bool RecalculateOnStart;
        public bool AlsoRecalculateTangents;

         [Tooltip("Cache asset will faster initialization")]
        public MeshDataCacheAsset DataCacheAsset;

        public List<SkinnedMeshRenderer> TargetSkinnedMeshRenderers;
        internal MeshDataCache _meshDataCache;
        private List<Mesh> _meshes;

        [Tooltip("Asset of this model in zero pose. Only necessary when using Calculate Blend Shapes option")]
        public List<GameObject> Prefabs;

        private List<GameObject> TempObjects;
        private List<Mesh> _tempMeshes;
        private List<SkinnedMeshRenderer> TempSMRs;
        private List<List<Material>> _materials;

        private List<ComputeBuffer> _normalBuffers;
        private List<ComputeBuffer> _tangentBuffers;
        private bool _isComputeBuffersCreated;


        private bool _isInitialized;

        private void Start()
        {
            Init();
        }

        public void Init()
        {
            if (_isInitialized)
            {
                OnDestroy();
            }

            var meshCount = TargetSkinnedMeshRenderers.Count;

            _meshes = new List<Mesh>(meshCount);
            TempObjects = new List<GameObject>(meshCount);
            TempSMRs = new List<SkinnedMeshRenderer>(meshCount);
            _tempMeshes = new List<Mesh>(meshCount);


            foreach (var smr in TargetSkinnedMeshRenderers)
            {
                _meshes.Add(smr.sharedMesh);
                _tempMeshes.Add(new Mesh());
            }

            _meshDataCache = new MeshDataCache();
            _meshDataCache.InitFromMultipleMesh(_meshes, AlsoRecalculateTangents);

            if (NormalOutputTarget == NormalOutputEnum.WriteToMesh)
            {
                foreach (var mesh in _meshes)
                    mesh.MarkDynamic();
            }
            else if (NormalOutputTarget == NormalOutputEnum.WriteToMaterial)
            {
                SetupForWriteToMaterial();
            }

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                var obj = Instantiate(Prefabs[meshIndex], transform);
                obj.SetActive(false);
                TempObjects.Add(obj);
                TempSMRs.Add(obj.GetComponentInChildren<SkinnedMeshRenderer>());
            }

            _isInitialized = true;
            if (RecalculateOnStart)
                RecalculateNormals();
        }

        private void SetupForWriteToMaterial()
        {
            var meshCount = TargetSkinnedMeshRenderers.Count;
            _normalBuffers = new List<ComputeBuffer>(meshCount);
            _tangentBuffers = new List<ComputeBuffer>(meshCount);
            _materials = new List<List<Material>>(meshCount);
            for (int i = 0; i < meshCount; i++)
            {
                var smr = TargetSkinnedMeshRenderers[i];
                var mats = new List<Material>(1);
                smr.GetMaterials(mats);
                _materials.Add(mats);
                var nBuffer = new ComputeBuffer(_meshes[i].vertexCount, sizeof(float) * 3);
                var tBuffer = new ComputeBuffer(_meshes[i].vertexCount, sizeof(float) * 4);
                _tangentBuffers.Add(tBuffer);
                _normalBuffers.Add(nBuffer);
                for (int matIndex = 0; matIndex < mats.Count; matIndex++)
                {
                    mats[matIndex].SetBuffer("normalsOutBuffer", nBuffer);
                    mats[matIndex].SetBuffer("tangentsOutBuffer", tBuffer);
                    mats[matIndex].SetFloat("_Initialized", 1);
                }
            }

            _meshDataCache.ApplyNormalsToBuffers(_normalBuffers);
            _isComputeBuffersCreated = true;
        }

        private void OnDestroy()
        {
            _meshDataCache.Dispose();

            //Compute buffers need to be destroyed
            if (_isComputeBuffersCreated)
            {
                foreach (var buffer in _normalBuffers)
                    buffer.Dispose();

                foreach (var buffer in _tangentBuffers)
                    buffer.Dispose();
            }

            foreach (var tempMesh in _tempMeshes)
            {
                Destroy(tempMesh);
            }

            foreach (var tempObject in TempObjects)
            {
                Destroy(tempObject);
            }
        }

        [ContextMenu("RecalculateNormals")]
        public void RecalculateNormals()
        {
            UpdateVertices();
            RecalculateCached();
        }

        private void RecalculateCached()
        {
            _meshDataCache.RecalculateNormals(Angle,AlsoRecalculateTangents);
            if (AlsoRecalculateTangents)
            {
                SetNormals();
                SetTangents();
            }
            else
            {
                SetNormals();
            }
        }

        private void SetNormals()
        {
            if (NormalOutputTarget == NormalOutputEnum.WriteToMesh)
                _meshDataCache.ApplyNormalsToMeshes(_meshes);
            else if (NormalOutputTarget == NormalOutputEnum.WriteToMaterial)
                _meshDataCache.ApplyNormalsToBuffers(_normalBuffers);
        }

        private void SetTangents()
        {
            if (NormalOutputTarget == NormalOutputEnum.WriteToMesh)
                _meshDataCache.ApplyTangentsToMeshes(_meshes);
            else if (NormalOutputTarget == NormalOutputEnum.WriteToMaterial)
                _meshDataCache.ApplyTangentsToMaterialBuffers(_tangentBuffers);
        }

        /// <summary>
        /// Vertex Data need to updated after blend shape changes.
        /// </summary>
        public void UpdateVertices()
        {
            Profiler.BeginSample("UpdateVertices");
            Profiler.BeginSample("TransferBlendShapeValuesAndBake");
            for (int meshIndex = 0; meshIndex < TargetSkinnedMeshRenderers.Count; meshIndex++)
            {
                var smr = TargetSkinnedMeshRenderers[meshIndex];
                for (int bsIndex = 0; bsIndex < smr.sharedMesh.blendShapeCount; bsIndex++)
                {
                    TempSMRs[meshIndex].SetBlendShapeWeight(bsIndex, smr.GetBlendShapeWeight(bsIndex));
                }

                Profiler.BeginSample("BakeMesh");
                TempSMRs[meshIndex].BakeMesh(_tempMeshes[meshIndex]);
                Profiler.EndSample();
            }

            Profiler.EndSample();

            var tempMDA = Mesh.AcquireReadOnlyMeshData(_tempMeshes);
            _meshDataCache.UpdateOnlyVertexData(tempMDA);
            tempMDA.Dispose();
            Profiler.EndSample();
        }
    }
}