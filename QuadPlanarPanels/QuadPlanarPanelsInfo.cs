using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace QuadPlanarPanels
{
    public class QuadPlanarPanelsInfo : GH_AssemblyInfo
    {
        public override string Name => "Quad Panels (Planar)";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("02a87409-7964-488e-a8fc-781c8df1d605");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}