﻿using AquaModelLibrary.Helpers.Readers;

namespace AquaModelLibrary.Data.BluePoint.CMDL
{
    public class CMDL
    {
        public int magic;
        public int unkInt0;
        public int unkId0;
        public CVariableTrail trail0 = null;
        public int unkInt1;

        public CVariableTrail matTrail = null;
        public ushort usht0;
        public byte unkBt0;
        public int unkInt2;

        //CMDLs start with a dictionary containing a cmsh material name and a cmat path. This dictionary uses cmsh material name as a key for easy mapping
        public List<CMDL_CMATMaterialMap> cmatReferences = new List<CMDL_CMATMaterialMap>();
        public CMDL_CMSHBorder border = null;
        public List<CMDL_CMSHReference> cmshReferences = new List<CMDL_CMSHReference>();

        public CMDL()
        {

        }
        public CMDL(BufferedStreamReaderBE<MemoryStream> sr)
        {
            magic = sr.Read<int>();
            unkInt0 = sr.Read<int>();
            unkId0 = sr.Read<int>();

            trail0 = new CVariableTrail(sr);

            unkInt1 = sr.Read<int>();
            matTrail = new CVariableTrail(sr);

            for (int i = 0; i < matTrail.data[matTrail.data.Count - 1]; i++)
            {
                cmatReferences.Add(new CMDL_CMATMaterialMap(sr));
            }
            border = new CMDL_CMSHBorder(sr);

            //for (int i = 0; i < border.cmshTrail.data[border.cmshTrail.data.Count - 1]; i++)
            var varTrail = border.clumps[border.clumps.Count - 1].trail.data;
            for (int i = 0; i < varTrail[varTrail.Count - 1]; i++)
            {
                cmshReferences.Add(new CMDL_CMSHReference(sr));
            }
        }
    }
}
