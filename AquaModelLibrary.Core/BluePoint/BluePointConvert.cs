﻿using AquaModelLibrary.Data.BluePoint.CANI;
using AquaModelLibrary.Data.BluePoint.CMAT;
using AquaModelLibrary.Data.BluePoint.CMDL;
using AquaModelLibrary.Data.BluePoint.CMSH;
using AquaModelLibrary.Data.BluePoint.CSKL;
using AquaModelLibrary.Data.PSO2.Aqua;
using AquaModelLibrary.Data.PSO2.Aqua.AquaNodeData;
using AquaModelLibrary.Data.PSO2.Aqua.AquaObjectData;
using AquaModelLibrary.Data.PSO2.Aqua.AquaObjectData.Intermediary;
using AquaModelLibrary.Helpers.MathHelpers;
using AquaModelLibrary.Helpers.Readers;
using AquaModelLibrary.Core.General;
using System.Numerics;

namespace AquaModelLibrary.Core.BluePoint
{
    public class BluePointConvert
    {
        public static void ReadFileTest(string filePath, out int start, out int typeFlags, out int modelType)
        {
            using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(filePath)))
            using (var streamReader = new BufferedStreamReaderBE<MemoryStream>(stream))
            {
                start = streamReader.Read<ushort>();
                streamReader.Seek(0x5, SeekOrigin.Begin);
                typeFlags = streamReader.Read<byte>();
                streamReader.Seek(0x9, SeekOrigin.Begin);
                modelType = streamReader.Read<byte>();
            }
        }

        public static List<byte> ReadFileTestVertDef(string filePath)
        {
            List<byte> bytes = new List<byte>();
            using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(filePath)))
            using (var streamReader = new BufferedStreamReaderBE<MemoryStream>(stream))
            {
                bool kill = false;
                while (true && streamReader.Position < stream.Length && kill == false)
                {
                    var posint1 = streamReader.Read<ushort>();
                    var posint2 = streamReader.Read<byte>();
                    if (posint1 == 0x4F53 && posint2 == 0x50)
                    {
                        while (true && streamReader.Position < stream.Length)
                        {
                            var int1 = streamReader.Read<ushort>();
                            var int2 = streamReader.Read<byte>();
                            streamReader.Seek(-3, SeekOrigin.Current);
                            if ((int1 == 0x524D && int2 == 0x4E) || (int1 == 0x4558 && int2 == 0x54))
                            {
                                kill = true;
                                break;
                            }
                            bytes.Add(streamReader.Read<byte>());
                        }
                    }
                    streamReader.Seek(-2, SeekOrigin.Current);
                }
            }

            return bytes;
        }

        public static void ReadCMDL(string filePath)
        {
            string rootPath = GetDeSRRootPath(filePath);
            string outPath = "";
            List<string> outNames = new List<string>();
            Dictionary<string, CMAT> materialDict = new Dictionary<string, CMAT>();
            List<string> cmshPaths = new List<string>();

            if (filePath.EndsWith(".cmdl"))
            {
                using (var streamReader = new BufferedStreamReaderBE<MemoryStream>(new MemoryStream(File.ReadAllBytes(filePath))))
                {
                    var cmdl = new CMDL(streamReader);
                    foreach (var cmatSet in cmdl.cmatReferences)
                    {
                        var cmatPath = Path.Combine(rootPath, cmatSet.cmatPath.str.Substring(2).Replace("****", "_cmn")).Replace("/", "\\");
                        using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(cmatPath)))
                        using (var sr = new BufferedStreamReaderBE<MemoryStream>(stream))
                        {
                            materialDict.Add(cmatSet.cmshMaterialName.str, new CMAT(sr));
                        }
                    }
                    foreach (var cmshPartialPath in cmdl.cmshReferences)
                    {
                        cmshPaths.Add(Path.Combine(rootPath, cmshPartialPath.cmshPath.str.Substring(2).Replace("****", "_cmn")).Replace("/", "\\"));
                        outNames.Add(Path.GetFileName(cmshPartialPath.cmshPath.str));
                    }
                }
                outPath = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outPath);
            }
            else if (filePath.EndsWith(".cmsh"))
            {
                outPath = Path.GetDirectoryName(filePath);
                outNames.Add(Path.GetFileName(filePath));
                cmshPaths.Add(filePath);
            }
            else
            {
                return;
            }

            for (int i = 0; i < cmshPaths.Count; i++)
            {
                var mshPath = cmshPaths[i];
                var outName = outNames[i];

                var aqp = ReadCMSH(mshPath, materialDict, out var aqn);
                aqp.ConvertToPSO2Model(true, false, false, true, false, false, false, true);
                aqp.ConvertToLegacyTypes();
                aqp.CreateTrueVertWeights();

                FbxExporterNative.ExportToFile(aqp, aqn, new List<AquaMotion>(), Path.Combine(outPath, Path.ChangeExtension(outName, ".fbx")), new List<string>(), new List<Matrix4x4>(), false);
            }

        }

        public static AquaObject ReadCMSH(string filePath, Dictionary<string, CMAT> materialDict, out AquaNode aqn)
        {
            string modelPath = Path.GetDirectoryName(filePath);
            string cmtlPath = Path.Combine(Path.GetDirectoryName(modelPath), "materials", "_cmn");
            using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(filePath)))
            using (var streamReader = new BufferedStreamReaderBE<MemoryStream>(stream))
            {
                return CMDLToAqua(new CMSH(streamReader), materialDict, cmtlPath, modelPath, out aqn);
            }
        }

        public static CANI ReadCANI(string filePath)
        {
            using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(filePath)))
            using (var streamReader = new BufferedStreamReaderBE<MemoryStream>(stream))
            {
                return new CANI(streamReader);
            }
        }

        public static AquaObject CMDLToAqua(CMSH mdl, Dictionary<string, CMAT> materialDict, string cmtlPath, string modelPath, out AquaNode aqn)
        {
            if (mdl.header.variantFlag2 == 0x41)
            {
                aqn = null;
                return null;
            }
            var csklPath = "";
            CSKL cskl = null;
            if (mdl.boneData != null && mdl.boneData.skeletonPath != null)
            {
                csklPath = Path.Combine(modelPath, Path.GetFileName(mdl.boneData.skeletonPath));
                if (File.Exists(csklPath))
                {
                    using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(csklPath)))
                    using (var streamReader = new BufferedStreamReaderBE<MemoryStream>(stream))
                    {
                        cskl = new CSKL(streamReader);
                    }
                }
                else
                {
                    if (modelPath.Contains("-app0"))
                    {
                        string rootPath = GetDeSRRootPath(modelPath);
                        csklPath = Path.Combine(rootPath, mdl.boneData.skeletonPath.Substring(2).Replace("****", "_cmn")).Replace("/", "\\");

                        using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(csklPath)))
                        using (var streamReader = new BufferedStreamReaderBE<MemoryStream>(stream))
                        {
                            cskl = new CSKL(streamReader);
                        }
                    }
                }
            }

            aqn = AquaNode.GenerateBasicAQN();
            AquaObject aqp = new AquaObject();
            if (cskl == null && mdl.boneData == null)
            {
                aqp.bonePalette.Add((uint)0);
            }
            else if (cskl == null && mdl.boneData != null)
            {
                aqn.nodeList.Clear();
                for (int i = 0; i < mdl.boneData.nameCount; i++)
                {
                    aqp.bonePalette.Add((uint)i);

                    //Try to make a skeleton from incomplete bone data. All we really get is a vector4 that seems like it *might* be a rotation? Can't do too much with this, but we'll put it in.
                    NODE aqNode = new NODE();
                    aqNode.boneShort1 = 0x1C0;
                    aqNode.animatedFlag = 1;
                    aqNode.parentId = i - 1;
                    aqNode.unkNode = -1;

                    var quat = mdl.boneData.boneVec4Array[i].ToQuat();
                    aqNode.eulRot = MathExtras.QuaternionToEuler(quat);
                    aqNode.scale = new Vector3(1, 1, 1);

                    var matrix = Matrix4x4.Identity;
                    matrix *= Matrix4x4.CreateScale(1, 1, 1);
                    var rotation = Matrix4x4.CreateFromQuaternion(quat);
                    matrix *= rotation;
                    matrix *= Matrix4x4.CreateTranslation(new Vector3());
                    Matrix4x4.Invert(matrix, out var invMat);

                    aqNode.SetInverseBindPoseMatrix(invMat);
                    aqNode.boneName.SetString(mdl.boneData.boneNames[i]);
                    aqn.nodeList.Add(aqNode);
                }
            }
            else
            {
                for (int i = 0; i < cskl.header.boneCount; i++)
                {
                    aqp.bonePalette.Add((uint)i);
                }
                aqn = new AquaNode();

                for (int i = 0; i < cskl.header.boneCount; i++)
                {
                    var metadata = cskl.metadata.familyIds[i];
                    var parentId = metadata.parentId;

                    var tfmMat = Matrix4x4.Identity;

                    Matrix4x4 mat = cskl.transforms[i].ComputeLocalTransform();
                    Matrix4x4 invMatReal = cskl.invTransforms[i];
                    //invMatReal = Matrix4x4.Transpose(invMatReal);
                    Matrix4x4.Invert(invMatReal, out var invInvMat);
                    mat *= tfmMat;

                    Matrix4x4.Decompose(mat, out var scale, out var quatRot, out var translation);

                    //If there's a parent, multiply by it
                    if (parentId != -1)
                    {
                        var pn = aqn.nodeList[parentId];
                        var parentInvTfm = new Matrix4x4(pn.m1.X, pn.m1.Y, pn.m1.Z, pn.m1.W,
                                                      pn.m2.X, pn.m2.Y, pn.m2.Z, pn.m2.W,
                                                      pn.m3.X, pn.m3.Y, pn.m3.Z, pn.m3.W,
                                                      pn.m4.X, pn.m4.Y, pn.m4.Z, pn.m4.W);

                        Matrix4x4.Invert(parentInvTfm, out var invParentInvTfm);
                        mat = mat * invParentInvTfm;
                    }
                    if (parentId == -1 && i != 0)
                    {
                        parentId = 0;
                    }

                    //Create AQN node
                    NODE aqNode = new NODE();
                    aqNode.boneShort1 = 0x1C0;
                    aqNode.animatedFlag = 1;
                    aqNode.parentId = parentId;
                    aqNode.unkNode = -1;

                    aqNode.pos = translation;
                    aqNode.eulRot = MathExtras.QuaternionToEuler(quatRot);
                    aqNode.scale = new Vector3(1, 1, 1);

                    Matrix4x4.Invert(mat, out var invMat);
                    aqNode.m1 = new Vector4(invMat.M11, invMat.M12, invMat.M13, invMat.M14);
                    aqNode.m2 = new Vector4(invMat.M21, invMat.M22, invMat.M23, invMat.M24);
                    aqNode.m3 = new Vector4(invMat.M31, invMat.M32, invMat.M33, invMat.M34);
                    aqNode.m4 = new Vector4(invMat.M41, invMat.M42, invMat.M43, invMat.M44);
                    aqNode.boneName.SetString(cskl.names.primaryNames.names[i].Split('|').Last());
                    //Debug.WriteLine($"{i} " + aqNode.boneName.GetString());
                    aqn.nodeList.Add(aqNode);
                }
            }

            var mesh = mdl;

            var nodeMatrix = Matrix4x4.Identity;

            //Vert data
            var vertCount = mesh.vertData.positionList.Count;
            VTXL vtxl = new VTXL();

            for (int v = 0; v < vertCount; v++)
            {
                vtxl.vertPositions.Add(mesh.vertData.positionList[v]);
                //vtxl.vertNormals.Add(Vector3.Transform(mesh.vertData.normals[v], mirrorMat));
                //var quat = mesh.vertData.normals[v];

                //UVs
                if (mesh.vertData.uvDict.ContainsKey(VertexMagic.TEX0))
                {
                    var uv1 = mesh.vertData.uvDict[VertexMagic.TEX0][v];
                    vtxl.uv1List.Add(new Vector2(uv1.X, uv1.Y));
                }
                if (mesh.vertData.uvDict.ContainsKey(VertexMagic.TEX1))
                {
                    var uv2 = mesh.vertData.uvDict[VertexMagic.TEX1][v];
                    vtxl.uv2List.Add(new Vector2(uv2.X, uv2.Y));
                }
                if (mesh.vertData.uvDict.ContainsKey(VertexMagic.TEX2))
                {
                    var uv3 = mesh.vertData.uvDict[VertexMagic.TEX2][v];
                    vtxl.uv3List.Add(new Vector2(uv3.X, uv3.Y));
                }
                if (mesh.vertData.uvDict.ContainsKey(VertexMagic.TEX3))
                {
                    var uv4 = mesh.vertData.uvDict[VertexMagic.TEX3][v];
                    vtxl.uv4List.Add(new Vector2(uv4.X, uv4.Y));
                }

                //Vert Colors
                if (mesh.vertData.colors.Count > 0)
                {
                    vtxl.vertColors.Add(mesh.vertData.colors[v]);
                }
                if (mesh.vertData.color2s.Count > 1)
                {
                    vtxl.vertColor2s.Add(mesh.vertData.color2s[v]);
                }

                if (mesh.vertData.vertWeights.Count > 0)
                {
                    vtxl.vertWeights.Add(mesh.vertData.vertWeights[v]);
                    vtxl.vertWeightIndices.Add(mesh.vertData.vertWeightIndices[v]);
                }
                else if (mesh.vertData.vertWeightIndices.Count > 0)
                {
                    vtxl.vertWeights.Add(new Vector4(1, 0, 0, 0));
                    vtxl.vertWeightIndices.Add(new int[] { mesh.vertData.vertWeightIndices[v][0], 0, 0, 0 });
                }
            }

            vtxl.convertToLegacyTypes();
            aqp.vtxeList.Add(VTXE.ConstructFromVTXL(vtxl, out int vc));

            //Face data

            //Do by vertex order if there's no faces
            if (mesh.faceData.faceList.Count == 0)
            {
                int faceCount = mesh.vertData.positionList.Count;
                for (int i = 0; i < faceCount; i += 3)
                {
                    mesh.faceData.faceList.Add(AquaModelLibrary.Data.DataTypes.Vector3Int.Vec3Int.CreateVec3Int(i, i + 1, i + 2));
                }

                //Assume mat face stuff is bad
                mesh.header.matList[0].startingFaceIndex = 0;
                mesh.header.matList[0].startingFaceVertIndex = 0;
                mesh.header.matList[0].endingFaceIndex = mesh.faceData.faceList.Count * 6;
                mesh.header.matList[0].faceVertIndicesUsed = mesh.faceData.faceList.Count * 3;

                for (int i = 1; i < mesh.header.matList.Count; i++)
                {
                    mesh.header.matList[i].startingFaceIndex = 0;
                    mesh.header.matList[i].startingFaceVertIndex = 0;
                    mesh.header.matList[i].endingFaceIndex = 0;
                    mesh.header.matList[i].faceVertIndicesUsed = 0;
                }
            }

            //Split CMSH by materials. Materials seem to contain a face count after which they split
            int currentFace = 0;
            for (int m = 0; m < mesh.header.matList.Count; m++)
            {
                int startFace;
                int faceCount;
                if (mesh.header.hasExtraFlags)
                {
                    startFace = mesh.header.matList[m].startingFaceIndex / 6;
                    faceCount = mesh.header.matList[m].endingFaceIndex / 6;
                }
                else //SOTC
                {
                    startFace = mesh.header.matList[m].startingFaceVertIndex / 3;
                    faceCount = mesh.header.matList[m].faceVertIndicesUsed / 3;
                }

                //Sometimes BluePoint's optimization led to degenerate faces, so we skip
                if (faceCount == 0 && mesh.header.matList[m].endingFaceIndex > 1)
                {
                    continue;
                }

                var baseMatName = mesh.header.matList[m];

                string texName = "test_d.dds";

                //This should be the primary way to get the material, but fall back to bruteforcing a bit if that fails
                if (materialDict.ContainsKey(baseMatName.matName))
                {
                    var cmat = materialDict[baseMatName.matName];
                    if (cmat.texNames.Count > 0)
                    {
                        texName = cmat.texNames[0];
                    }
                }
                else
                {
                    var matFileName = baseMatName.matName.Replace("_mat1", "");
                    matFileName = matFileName.Replace("_mat", "");
                    var matPath = Path.Combine(cmtlPath, matFileName + ".cmat");
                    var backupMatPath = Path.Combine(modelPath, matFileName + ".cmat");
                    if (File.Exists(matPath))
                    {
                        using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(matPath)))
                        using (var streamReader = new BufferedStreamReaderBE<MemoryStream>(stream))
                        {
                            var cmat = new CMAT(streamReader);
                            if (cmat.texNames.Count > 0)
                            {
                                texName = cmat.texNames[0];
                            }
                        }
                    }
                    else if (File.Exists(backupMatPath))
                    {
                        using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(backupMatPath)))
                        using (var streamReader = new BufferedStreamReaderBE<MemoryStream>(stream))
                        {
                            var cmat = new CMAT(streamReader);
                            if (cmat.texNames.Count > 0)
                            {
                                texName = cmat.texNames[0];
                            }
                        }

                    }
                }

                //Material
                var mat = new GenericMaterial();
                mat.matName = $"{mesh.header.matList[m].matName}";
                mat.texNames = new List<string>
                {
                    texName
                };
                aqp.tempMats.Add(mat);

                if ((mesh.header.matList[m].startingFaceIndex == 0 && mesh.header.matList[m].endingFaceIndex == 0) || (mesh.header.matList[m].startingFaceIndex == -1 && mesh.header.matList[m].endingFaceIndex == -1))
                {
                    startFace = currentFace;
                    faceCount = mesh.faceData.faceList.Count - currentFace;
                }
                currentFace = startFace + faceCount;

                Dictionary<int, int> vertIdDict = new Dictionary<int, int>();
                VTXL matVtxl = new VTXL();
                GenericTriangles genMesh = new GenericTriangles();
                List<Vector3> triList = new List<Vector3>();
                for (int f = startFace; f < (startFace + faceCount); f++)
                {
                    var tri = mesh.faceData.faceList[f];

                    int x;
                    int y;
                    int z;
                    if (vertIdDict.TryGetValue(tri.X, out var value))
                    {
                        x = value;
                    }
                    else
                    {
                        vertIdDict.Add(tri.X, matVtxl.vertPositions.Count);
                        x = matVtxl.vertPositions.Count;
                        VTXL.AppendVertex(vtxl, matVtxl, tri.X);
                    }
                    if (vertIdDict.TryGetValue(tri.Y, out var value2))
                    {
                        y = value2;
                    }
                    else
                    {
                        vertIdDict.Add(tri.Y, matVtxl.vertPositions.Count);
                        y = matVtxl.vertPositions.Count;
                        VTXL.AppendVertex(vtxl, matVtxl, tri.Y);
                    }
                    if (vertIdDict.TryGetValue(tri.Z, out var value3))
                    {
                        z = value3;
                    }
                    else
                    {
                        vertIdDict.Add(tri.Z, matVtxl.vertPositions.Count);
                        z = matVtxl.vertPositions.Count;
                        VTXL.AppendVertex(vtxl, matVtxl, tri.Z);
                    }

                    //Avoid degen tris
                    if (x == y || x == z || y == z)
                    {
                        continue;
                    }
                    triList.Add(new Vector3(x, y, z));
                }
                genMesh.triList = triList;

                //Extra
                genMesh.vertCount = matVtxl.vertPositions.Count;
                genMesh.matIdList = new List<int>(new int[genMesh.triList.Count]);
                for (int j = 0; j < genMesh.matIdList.Count; j++)
                {
                    genMesh.matIdList[j] = aqp.tempMats.Count - 1;
                }

                if (genMesh.vertCount > 0)
                {
                    aqp.tempTris.Add(genMesh);
                    aqp.vtxlList.Add(matVtxl);
                }
            }


            return aqp;
        }

        private static string GetDeSRRootPath(string currentPath)
        {
            int i = 0;
            while (Path.GetFileName(currentPath).IndexOf("-app0") == -1)
            {
                currentPath = Path.GetDirectoryName(currentPath);
                i++;
                //Should seriously never ever ever happen, but screw it
                if (i == 255)
                {
                    break;
                }
            }

            return currentPath;
        }
    }
}
