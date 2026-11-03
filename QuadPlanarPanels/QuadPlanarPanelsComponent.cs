using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.UI;
using System;
using System.Collections.Generic;

namespace QuadPlanarPanels
{
    public class QuadPlanarPanelsComponent : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public QuadPlanarPanelsComponent()
          : base("Quad Panels (Planar)", "QP",
            "Create quad planar panels on the freeform surface.",
            "C#", "Paneling")
        {
        }


        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddSurfaceParameter("Surface", "S", "Input surface", GH_ParamAccess.item);
            pManager.AddIntegerParameter("U", "U", "U direction division number", GH_ParamAccess.item);
            pManager.AddIntegerParameter("V", "V", "V direction division number", GH_ParamAccess.item);
            pManager.AddNumberParameter("Smooth", "S", "Smooth factor", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Panels", "P", "Output panels", GH_ParamAccess.list);
            pManager.AddMeshParameter("Mesh", "M", "Output mesh", GH_ParamAccess.item);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //intput
            Surface surface = null;
            double smooth = 0.0;
            int u = 0;
            int v = 0;

            //output
            List<Brep> panelList = new List<Brep>();
            Mesh mesh = new Mesh();

            DA.GetData(0, ref surface);
            DA.GetData(1, ref u);
            DA.GetData(2, ref v);
            DA.GetData(3, ref smooth);


            // 1. Validate inputs
            if (surface == null)
            {
                return;
            }

            // Smooth between 0.0 and 1.0
            smooth = Math.Max(0.0, Math.Min(1.0, smooth));

            // 2. Get surface UV domains and estimate division counts
            Interval uDom = surface.Domain(0);
            Interval vDom = surface.Domain(1);

            Point3d p00 = surface.PointAt(uDom.Min, vDom.Min);
            Point3d p10 = surface.PointAt(uDom.Max, vDom.Min);
            Point3d p01 = surface.PointAt(uDom.Min, vDom.Max);

            double width = p00.DistanceTo(p10);
            double height = p00.DistanceTo(p01);

            int uCount = u;
            int vCount = v;

            double uStep = uDom.Length / uCount;
            double vStep = vDom.Length / vCount;

            // 3. Generate original raw surface grid
            Point3d[,] origGrid = new Point3d[uCount + 1, vCount + 1];
            for (int i = 0; i <= uCount; i++)
            {
                double _u = uDom.Min + i * uStep;
                if (i == uCount) _u = uDom.Max;

                for (int j = 0; j <= vCount; j++)
                {
                    double _v = vDom.Min + j * vStep;
                    if (j == vCount) _v = vDom.Max;

                    origGrid[i, j] = surface.PointAt(_u, _v);
                }
            }

            // 4. Compute grid
            Point3d[,] relaxedGrid = (Point3d[,])origGrid.Clone();
            int iterations = 12;

            for (int iter = 0; iter < iterations; iter++)
            {
                Plane[,] facePlanes = new Plane[uCount, vCount];
                bool[,] planeValid = new bool[uCount, vCount];

                for (int i = 0; i < uCount; i++)
                {
                    for (int j = 0; j < vCount; j++)
                    {
                        List<Point3d> qPts = new List<Point3d> {
                    relaxedGrid[i, j],
                    relaxedGrid[i + 1, j],
                    relaxedGrid[i + 1, j + 1],
                    relaxedGrid[i, j + 1]
                };

                        Plane fitPlane;
                        if (Plane.FitPlaneToPoints(qPts, out fitPlane) == PlaneFitResult.Success)
                        {
                            facePlanes[i, j] = fitPlane;
                            planeValid[i, j] = true;
                        }
                        else
                        {
                            planeValid[i, j] = false;
                        }
                    }
                }

                Point3d[,] nextGrid = new Point3d[uCount + 1, vCount + 1];
                for (int i = 0; i <= uCount; i++)
                {
                    for (int j = 0; j <= vCount; j++)
                    {
                        List<Plane> adjacentPlanes = new List<Plane>();
                        if (i > 0 && j > 0 && planeValid[i - 1, j - 1]) adjacentPlanes.Add(facePlanes[i - 1, j - 1]);
                        if (i < uCount && j > 0 && planeValid[i, j - 1]) adjacentPlanes.Add(facePlanes[i, j - 1]);
                        if (i < uCount && j < vCount && planeValid[i, j]) adjacentPlanes.Add(facePlanes[i, j]);
                        if (i > 0 && j < vCount && planeValid[i - 1, j]) adjacentPlanes.Add(facePlanes[i - 1, j]);

                        Point3d targetPos = relaxedGrid[i, j];
                        if (adjacentPlanes.Count > 0)
                        {
                            Vector3d sumProj = new Vector3d(0, 0, 0);
                            foreach (Plane pl in adjacentPlanes)
                            {
                                Point3d proj = pl.ClosestPoint(relaxedGrid[i, j]);
                                sumProj += new Vector3d(proj);
                            }
                            targetPos = new Point3d(sumProj / adjacentPlanes.Count);
                        }
                        nextGrid[i, j] = targetPos;
                    }
                }
                relaxedGrid = nextGrid;
            }

            // 5. Merge raw surface grid and relaxed planar grid using manual coordinate interpolation
            Point3d[,] finalGrid = new Point3d[uCount + 1, vCount + 1];
            for (int i = 0; i <= uCount; i++)
            {
                for (int j = 0; j <= vCount; j++)
                {
                    Point3d a = origGrid[i, j];
                    Point3d b = relaxedGrid[i, j];

                    finalGrid[i, j] = new Point3d(
                        a.X + (b.X - a.X) * smooth,
                        a.Y + (b.Y - a.Y) * smooth,
                        a.Z + (b.Z - a.Z) * smooth
                    );
                }
            }

            // 6. Build Preview Mesh and Planar Brep Panels
            for (int i = 0; i <= uCount; i++)
            {
                for (int j = 0; j <= vCount; j++)
                {
                    mesh.Vertices.Add(finalGrid[i, j]);
                }
            }

            for (int i = 0; i < uCount; i++)
            {
                for (int j = 0; j < vCount; j++)
                {
                    int idx0 = i * (vCount + 1) + j;
                    int idx1 = (i + 1) * (vCount + 1) + j;
                    int idx2 = (i + 1) * (vCount + 1) + (j + 1);
                    int idx3 = i * (vCount + 1) + (j + 1);

                    mesh.Faces.AddFace(idx0, idx1, idx2, idx3);

                    Point3d ptA = finalGrid[i, j];
                    Point3d ptB = finalGrid[i + 1, j];
                    Point3d ptC = finalGrid[i + 1, j + 1];
                    Point3d ptD = finalGrid[i, j + 1];

                    List<Point3d> quadPts = new List<Point3d> { ptA, ptB, ptC, ptD };
                    Plane fitPlane;
                    if (Plane.FitPlaneToPoints(quadPts, out fitPlane) == PlaneFitResult.Success)
                    {
                        Point3d pA = fitPlane.ClosestPoint(ptA);
                        Point3d pB = fitPlane.ClosestPoint(ptB);
                        Point3d pC = fitPlane.ClosestPoint(ptC);
                        Point3d pD = fitPlane.ClosestPoint(ptD);

                        Brep panel = Brep.CreateFromCornerPoints(pA, pB, pC, pD, 1e-3);
                        if (panel != null)
                        {
                            panelList.Add(panel);
                        }
                    }
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();


            DA.SetDataList(0, panelList);
            DA.SetData(1, mesh);

        }


        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("abc4cd9c-5472-4e5d-a74e-66bdecea7d3c");
    } 
    
}