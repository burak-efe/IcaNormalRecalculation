﻿using System;
using System.Collections.Generic;
using Ica.Utils;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Ica.Normal
{
    /// <summary>
    /// A big data container that hold mesh data (or merged data of list of meshes) for needed to normal and tangent calculation
    /// </summary>
    public class MeshDataCache : IDisposable
    {
        public int TotalVertexCount;
        public int TotalIndexCount;

        //these are need for normal 
        public NativeList<float3> VertexData;
        public NativeList<int> IndexData;
        public NativeList<float3> NormalData;
        public NativeList<int> AdjacencyList;
        public NativeList<int> AdjacencyMapper;
        public NativeList<int> ConnectedCountMapper;
        private NativeList<int> _vertexSeparatorData;
        private NativeList<int> _indexSeparatorData;

        //these are need for tangent
        public NativeList<float4> TangentData;
        public NativeList<float3> Tan1Data;
        public NativeList<float3> Tan2Data;
        public NativeList<float2> UVData;
        public NativeList<float3> TriNormalData;

        private Mesh.MeshDataArray _mda;
        private bool _initialized;
        private bool _cachedForTangents;

        public void InitFromMultipleMesh(List<Mesh> meshes, bool cacheForTangents)
        {
            Dispose();
            _mda = Mesh.AcquireReadOnlyMeshData(meshes);

            //separators
            _vertexSeparatorData = new NativeList<int>(_mda.Length + 1, Allocator.Persistent);
            _indexSeparatorData = new NativeList<int>(_mda.Length + 1, Allocator.Persistent);

            //Counts
            _mda.GetTotalVertexCountFomMDA(out TotalVertexCount);
            _mda.GetTotalIndexCountOfMDA(out TotalIndexCount);


            VertexData = new NativeList<float3>(TotalVertexCount, Allocator.Persistent);
            _mda.GetMergedVertices(ref VertexData, ref _vertexSeparatorData);

            IndexData = new NativeList<int>(TotalIndexCount, Allocator.Persistent);
            _mda.GetMergedIndices(ref IndexData, ref _indexSeparatorData);

            NormalData = new NativeList<float3>(TotalVertexCount, Allocator.Persistent);
            _mda.GetMergedNormals(ref NormalData, ref _vertexSeparatorData);

            TriNormalData = new NativeList<float3>(TotalIndexCount / 3, Allocator.Persistent);
            TriNormalData.Resize(TotalIndexCount / 3, NativeArrayOptions.UninitializedMemory);

            VertexPositionMapper.GetVertexPosHashMap(VertexData.AsArray(), out var tempPosGraph, Allocator.Temp);
            Normal.AdjacencyMapper.CalculateAdjacencyData(VertexData.AsArray(), IndexData.AsArray(), tempPosGraph, out AdjacencyList, out AdjacencyMapper, out ConnectedCountMapper,
                Allocator.Persistent);

            if (cacheForTangents)
            {
                TangentData = new NativeList<float4>(TotalVertexCount, Allocator.Persistent);
                _mda.GetMergedTangents(ref TangentData, ref _vertexSeparatorData);
                UVData = new NativeList<float2>(TotalVertexCount, Allocator.Persistent);
                _mda.GetMergedUVs(ref UVData, ref _vertexSeparatorData);
                Tan1Data = new NativeList<float3>(TotalIndexCount / 3, Allocator.Persistent);
                Tan2Data = new NativeList<float3>(TotalIndexCount / 3, Allocator.Persistent);
                _cachedForTangents = true;
            }

            Assert.IsTrue(VertexData.Length == TotalVertexCount);
            Assert.IsTrue(IndexData.Length == TotalIndexCount);
            Assert.IsTrue(TriNormalData.Length == IndexData.Length / 3);

            _initialized = true;
        }

        public void UpdateOnlyVertexData(in Mesh.MeshDataArray mda)
        {
            Profiler.BeginSample("UpdateOnlyVertexData");
            mda.GetMergedVertices(ref VertexData, ref _vertexSeparatorData);
            Profiler.EndSample();
        }

        public UnsafeList<NativeList<float3>> GetSplitNormalData(Allocator allocator)
        {
            var n = new UnsafeList<NativeList<float3>>(_mda.Length, allocator);
            for (int meshIndex = 0; meshIndex < _mda.Length; meshIndex++)
            {
                var meshNormal = new NativeList<float3>(_vertexSeparatorData[meshIndex + 1] - _vertexSeparatorData[meshIndex], allocator);
                meshNormal.CopyFrom(NormalData.AsArray().GetSubArray(_vertexSeparatorData[meshIndex], _vertexSeparatorData[meshIndex + 1] - _vertexSeparatorData[meshIndex]));
                n.Add(meshNormal);
            }

            return n;
        }

        public UnsafeList<NativeList<float4>> GetTempSplitTangentData()
        {
            var splitTangentData = new UnsafeList<NativeList<float4>>(_mda.Length, Allocator.Temp);
            for (int meshIndex = 0; meshIndex < _mda.Length; meshIndex++)
            {
                var tempTangent = new NativeList<float4>(Allocator.Temp);
                tempTangent.CopyFrom(TangentData.AsArray().GetSubArray(_vertexSeparatorData[meshIndex], _vertexSeparatorData[meshIndex + 1] - _vertexSeparatorData[meshIndex]));
                splitTangentData.Add(tempTangent);
            }

            return splitTangentData;
        }

        public void ApplyNormalsToBuffers(List<ComputeBuffer> buffers)
        {
            Profiler.BeginSample("ApplyNormalsToBuffers");
            for (int meshIndex = 0; meshIndex < buffers.Count; meshIndex++)
            {
                buffers[meshIndex].SetData(
                    NormalData.AsArray().GetSubArray(_vertexSeparatorData[meshIndex], _vertexSeparatorData[meshIndex + 1] - _vertexSeparatorData[meshIndex])
                );
            }

            Profiler.EndSample();
        }

        public void ApplyTangentsToMaterialBuffers(List<ComputeBuffer> buffers)
        {
            for (int meshIndex = 0; meshIndex < buffers.Count; meshIndex++)
            {
                buffers[meshIndex].SetData(
                    TangentData.AsArray().GetSubArray(_vertexSeparatorData[meshIndex], _vertexSeparatorData[meshIndex + 1] - _vertexSeparatorData[meshIndex])
                );
            }
        }

        public void ApplyNormalsToMeshes(List<Mesh> meshes)
        {
            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                meshes[meshIndex].SetNormals(
                    NormalData.AsArray().GetSubArray(_vertexSeparatorData[meshIndex], _vertexSeparatorData[meshIndex + 1] - _vertexSeparatorData[meshIndex])
                );
            }
        }

        public void ApplyTangentsToMeshes(List<Mesh> meshes)
        {
            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                meshes[meshIndex].SetTangents(
                    TangentData.AsArray().GetSubArray(_vertexSeparatorData[meshIndex], _vertexSeparatorData[meshIndex + 1] - _vertexSeparatorData[meshIndex])
                );
            }
        }

        // if there is a persistent allocate, there should be dispose #deepProgrammingQuotes #nativeLifeHacks
        public void Dispose()
        {
            if (_initialized == false)
                return;
            _initialized = false;
            _mda.Dispose();
            VertexData.Dispose();
            IndexData.Dispose();
            NormalData.Dispose();
            AdjacencyList.Dispose();
            AdjacencyMapper.Dispose();
            _vertexSeparatorData.Dispose();
            _indexSeparatorData.Dispose();
            TriNormalData.Dispose();

            if (_cachedForTangents)
            {
                _cachedForTangents = false;
                TangentData.Dispose();
                UVData.Dispose();
                Tan1Data.Dispose();
                Tan2Data.Dispose();
            }
        }

        // try something this to remove requirement of read/ write enabled
        public static Mesh MakeReadableMeshCopy(Mesh nonReadableMesh)
        {
            Mesh meshCopy = new Mesh();
            meshCopy.indexFormat = nonReadableMesh.indexFormat;

            // Handle vertices
            GraphicsBuffer verticesBuffer = nonReadableMesh.GetVertexBuffer(0);
            int totalSize = verticesBuffer.stride * verticesBuffer.count;
            byte[] data = new byte[totalSize];
            verticesBuffer.GetData(data);
            meshCopy.SetVertexBufferParams(nonReadableMesh.vertexCount, nonReadableMesh.GetVertexAttributes());
            meshCopy.SetVertexBufferData(data, 0, 0, totalSize);
            verticesBuffer.Release();

            // Handle triangles
            meshCopy.subMeshCount = nonReadableMesh.subMeshCount;
            GraphicsBuffer indexesBuffer = nonReadableMesh.GetIndexBuffer();
            int tot = indexesBuffer.stride * indexesBuffer.count;
            byte[] indexesData = new byte[tot];
            indexesBuffer.GetData(indexesData);
            meshCopy.SetIndexBufferParams(indexesBuffer.count, nonReadableMesh.indexFormat);
            meshCopy.SetIndexBufferData(indexesData, 0, 0, tot);
            indexesBuffer.Release();

            // Restore submesh structure
            uint currentIndexOffset = 0;
            for (int i = 0; i < meshCopy.subMeshCount; i++)
            {
                uint subMeshIndexCount = nonReadableMesh.GetIndexCount(i);
                meshCopy.SetSubMesh(i, new SubMeshDescriptor((int)currentIndexOffset, (int)subMeshIndexCount));
                currentIndexOffset += subMeshIndexCount;
            }

            // Recalculate normals and bounds
            meshCopy.RecalculateNormals();
            meshCopy.RecalculateBounds();

            return meshCopy;
        }
    }
}