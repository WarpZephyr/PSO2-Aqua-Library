﻿using AquaModelLibrary.Data.DataTypes.SetLengthStrings;
using System.Numerics;

namespace AquaModelLibrary.Data.PSO2.Aqua.AquaNodeData
{
    /*
        * BoneShort1 values:
        * Disable PositionX Inheritance? 0x1
        * Disable PositionY Inheritance? 0x2
        * Disable PositionZ Inheritance? 0x4
        * Disable RotationX Inheritance  0x8
        * Disable RotationY Inheritance  0x10
        * Disable RotationZ Inheritance  0x20
        * Disable ScaleX Inheritance     0x40
        * Disable ScaleY Inheritance     0x80
        * Disable ScaleZ Inheritance     0x100
        * Disable Positional Animation   0x200
        * Disable Rotational Animation   0x400
        * ?                              0x800
        * ?                              0x1000
        * ?                              0x2000
        * Disable Node?                  0x4000
        * ?                              0x8000
        * 
        * Default is typically 0x1C0, which disables all scale inheritance from the parent. Many of these seem hardcoded off during gameplay, at least on the player. The salon allows the enablement of the 0x8-0x100
    */

    /*
     * BoneShort2 values:
     *                                0x1
     *                                0x2
     *                                0x4
     *                                0x8
     *                                0x10
     *                                0x20
     *                                0x40
     *                                0x80
     *                                0x100
     *                                0x200
     *                                0x400
     *                                0x800
     *                                0x1000
     *                                0x2000
     *                                0x4000
     *                                0x8000
     * 
     * Default is typically 0x1C0, which disables all scale inheritance from the parent. Many of these seem hardcoded off during gameplay, at least on the player. The salon allows the enablement of the 0x8-0x100
    */

    //Technically, the structs in this are all in the one big NODE struct, but it seemed more logical to separate it out a bit more.
    public struct NODE
    {
        public ushort boneShort1; //0x3, type 0x9 //First 2 bytes of this.
        public ushort boneShort2; //Latter 2 bytes of above. Bones with 0x400 in this value are not exported in animations.
        public int animatedFlag;  //0xB, type 0x9 //Should generally be 1. I assume this is what it is based on PSU's bone format. Never really used normally?
        public int parentId;      //0x4, type 0x8
        public int unkNode;       //0xF, type 0x8 //Always observed -1

        public int firstChild;    //0x5, type 0x8
        public int nextSibling;   //0x6, type 0x8
        public int const0_2;      //0xC, type 0x9? Ordering and nebulous usage makes this unclear. Observed only 0.
        public int bool_1C;       //Counts up in NGS era models for unknown reasons. 

        public Vector3 pos;       //0x7, type 0x4A, 0x1
        public int const0_4;
        public Vector3 eulRot;    //0x8, type 0x4A, 0x1 //Rotation order changes for unknown reasons
        public int const0_5;
        public Vector3 scale;     //0x9, type 0x4A, 0x1
        public int const0_6;
        // 4x4 Matrix             //0xA, type 0xCA, 0xA, 0x3
        public Vector4 m1;
        public Vector4 m2;
        public Vector4 m3;
        public Vector4 m4;
        public PSO2String boneName; //0xD, type 0x2

        public Matrix4x4 GetInverseBindPoseMatrix()
        {
            var bnMat = new Matrix4x4(m1.X, m1.Y, m1.Z, m1.W,
                                        m2.X, m2.Y, m2.Z, m2.W,
                                        m3.X, m3.Y, m3.Z, m3.W,
                                        m4.X, m4.Y, m4.Z, m4.W);
            return bnMat;
        }

        public Matrix4x4 GetInverseBindPoseMatrixInverted()
        {
            var bnMat = new Matrix4x4(m1.X, m1.Y, m1.Z, m1.W,
                                        m2.X, m2.Y, m2.Z, m2.W,
                                        m3.X, m3.Y, m3.Z, m3.W,
                                        m4.X, m4.Y, m4.Z, m4.W);
            Matrix4x4.Invert(bnMat, out Matrix4x4 result);
            return result;
        }

        public void SetInverseBindPoseMatrix(Matrix4x4 bnMat)
        {
            m1 = new Vector4(bnMat.M11, bnMat.M12, bnMat.M13, bnMat.M14);
            m2 = new Vector4(bnMat.M21, bnMat.M22, bnMat.M23, bnMat.M24);
            m3 = new Vector4(bnMat.M31, bnMat.M32, bnMat.M33, bnMat.M34);
            m4 = new Vector4(bnMat.M41, bnMat.M42, bnMat.M43, bnMat.M44);
        }
    }
}
