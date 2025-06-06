﻿//
// BIM IFC library: this library works with Autodesk(R) Revit(R) to export IFC files containing model geometry.
// Copyright (C) 2012  Autodesk, Inc.
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;
using Revit.IFC.Export.Exporter;
using Revit.IFC.Export.Toolkit;

namespace Revit.IFC.Export.Utility
{
   /// <summary>
   /// Provides static methods for geometry related manipulations.
   /// </summary>
   public class GeometryUtil
   {
      /// <summary>
      /// An enum used in CreateIFCCurveFromRevitCurve to determine how to create
      /// the Revit curve in IFC entities.
      /// </summary>
      public enum TrimCurvePreference
      {
         BaseCurve,              // Do not trim the curve.
         TrimmedCurve,           // Use a base curve and trim the curve
         UsePolyLineOrTrim,      // Use a polyline for a bounded line, otherwise trim.
      }
      /// <summary>
      /// The comparer for comparing XYZ.
      /// </summary>
      public struct XYZComparer : IComparer<XYZ>
      {
         /// <summary>
         /// Check if 2 XYZ values are almost equal, such that for each component n of x and y, |x[n]-y[n]| < MathUtil.Eps().
         /// </summary>
         /// <param name="x">The first XYZ value.</param>
         /// <param name="y">The second XYZ value.</param>
         /// <returns>-1 if x is less than y, 1 if x is greater than y, and 0 if x is almost equal to y.</returns>
         public int Compare(XYZ x, XYZ y)
         {
            double eps = MathUtil.Eps();
            if (x.X < y.X - eps)
               return -1;
            if (x.X > y.X + eps)
               return 1;
            if (x.Y < y.Y - eps)
               return -1;
            if (x.Y > y.Y + eps)
               return 1;
            if (x.Z < y.Z - eps)
               return -1;
            if (x.Z > y.Z + eps)
               return 1;
            return 0;
         }
      }

      /// <summary>
      /// The EqualityComparer for comparing EdgeEndPoint.
      /// </summary>
      public class EdgeEndPointComparer : EqualityComparer<EdgeEndPoint>
      {
         /// <summary>
         /// Check if two EdgeEndPoints are equal by checking that IDs of corresponding edges and indices of corresponding endpoints are equal.
         /// </summary>
         /// <param name="edgePnt1">The first EdgeEndPoint value.</param>
         /// <param name="edgePnt2">The second EdgeEndPoint value.</param>
         /// <returns>True if both EdgeEndPoint refer to the same Edge and the same endpoint of that Edge</returns>
         public override bool Equals(EdgeEndPoint edgePnt1, EdgeEndPoint edgePnt2)
         {
            return edgePnt1.Edge.Id == edgePnt2.Edge.Id && edgePnt1.Index == edgePnt2.Index;
         }

         /// <summary>
         /// Calculate hash code for EdgeEndPoint.
         /// </summary>
         /// <param name="edgePnt">The EdgeEndPoint for which the hash code will be calculated.</param>
         /// <returns>Hash code of edgePnt as a combination of hash codes of its corresponding Edge ID and endpoint</returns>
         public override int GetHashCode(EdgeEndPoint edgePnt)
         {
            return edgePnt.Edge.Id.GetHashCode() ^ edgePnt.Index.GetHashCode();
         }
      }

      /// <summary>
      /// Stores vertices list of geometry primitives.
      /// Derived classes concretize the actual primitive type the vertices list belong to.
      /// </summary>
      private abstract class PrimVertices
      {
         public IFCAnyHandleUtil.IfcPointList PointList { get; protected set; } = new IFCAnyHandleUtil.IfcPointList();
         public PointBase this[int key]
         {
            get => PointList[key];
            set => PointList[key] = value;
         }
         public PointBase Last() { return PointList.Last(); }
      }
      private class PolyLineVertices : PrimVertices
      {
         public PolyLineVertices(UV beg, UV end)
         {
            PointList.AddPoints(beg, end);
         }
         public PolyLineVertices(XYZ beg, XYZ end)
         {
            PointList.AddPoints(beg, end);
         }
         public PolyLineVertices(List<XYZ> points)
         {
            if (points.Count < 2)
               throw new ArgumentException("Number of points must be 2 or greater");

            PointList.AddPoints(points);
         }
         public PolyLineVertices(List<UV> points)
         {
            if (points.Count < 2)
               throw new ArgumentException("Number of points must be 2 or greater");

            PointList.AddPoints(points);
         }
      }
      private class ArcVertices : PrimVertices
      {
         public ArcVertices(UV start, UV mid, UV end)
         {
            PointList.AddPoints(start, mid, end);
         }
         public ArcVertices(XYZ start, XYZ mid, XYZ end)
         {
            PointList.AddPoints(start, mid, end);
         }
      }
      /// <summary>
      /// Stores indices list used to build geometry primitives.
      /// Derived classes concretize the primitive type defined by indices.
      /// </summary>
      public abstract class SegmentIndices
      {
         public abstract int GetStart();
         public abstract int GetEnd();
         public abstract void SetEnd(int endIndex);
         public abstract void CalcIndices(int startIdx);
         public bool IsCalculated { get; protected set; } = false;
         public virtual bool TryMerge(SegmentIndices segmentIndices)
         {
            return false;
         }
      }
      public class PolyLineIndices : SegmentIndices
      {
         public PolyLineIndices(int lineCount)
         { LineCount = lineCount; }
         public List<int> Indices { get; protected set; }
         public int m_lineCount = 0;
         public int LineCount
         {
            get { return m_lineCount; }
            //Indices are not valid because line count has been changed. So delete indince list.
            set { m_lineCount = value; IsCalculated = false; }
         }
         public override int GetStart()
         { return Indices[0]; }
         public override int GetEnd()
         { return Indices.Last(); }
         public override void SetEnd(int endIndex)
         { Indices[Indices.Count - 1] = endIndex; }
         public override void CalcIndices(int startIdx)
         {
            Indices = new List<int> { startIdx };
            Indices.AddRange(Enumerable.Range(startIdx, LineCount).Select(x => x + 1));
            IsCalculated = true;
         }
         //Function assumes that input and this segmants are consecutive. End index of this segmant must be equal to start index of input segment.
         public override bool TryMerge(SegmentIndices segmentIndices)
         {
            PolyLineIndices inputPolyLineIndices = segmentIndices as PolyLineIndices;
            if (segmentIndices == this || inputPolyLineIndices == null)
               return false;

            //Indices array after merge must not contain duplicated indices.
            //So remove either End index of this segment or Start index of input segment.
            Indices.RemoveAt(Indices.Count - 1);
            Indices.AddRange(inputPolyLineIndices.Indices);
            return true;
         }
      }
      public class ArcIndices : SegmentIndices
      {
         public int Start { get; protected set; }
         public int Mid { get; protected set; }
         public int End { get; protected set; }
         public override int GetStart()
         { return Start; }
         public override int GetEnd()
         { return End; }
         public override void SetEnd(int endIndex)
         { End = endIndex; }
         public override void CalcIndices(int startIdx)
         {
            Start = startIdx;
            Mid = startIdx + 1;
            End = startIdx + 2;
            IsCalculated = true;
         }
      }
      /// <summary>
      /// Accumulates vertices and calculates indices.
      /// This class assumes that primitive vertices should be connected in the order they appear in the array.
      /// Based on this assumption this class constructs indices array.
      /// </summary>
      private class PolyCurve
      {
         private List<PrimVertices> PrimVerticesList { get; set; } = new List<PrimVertices>();
         public bool PolyLinesOnly { get; protected set; } = true;
         //PointList and SegmentsIndices are built by BuildVerticesAndIndices()
         public IFCAnyHandleUtil.IfcPointList PointList { get; protected set; } = new IFCAnyHandleUtil.IfcPointList();
         public List<SegmentIndices> SegmentsIndices { get; protected set; } = null;

         public PolyCurve(PrimVertices primVertices)
         {
            if (!AddPrimVertices(primVertices))
               throw new ArgumentException("Error when constructing PolyCurve");
         }

         public void BuildVerticesAndIndices()
         {
            foreach (PrimVertices primVertices in PrimVerticesList)
            {
               const int MinIndexValue = 1;

               //1. Build point list.
               //Current poly curve's end point and start point of primitive curve are always equal.
               //So remove one of them from point list.
               if (PointList.Count != 0)
                  PointList.Points.RemoveAt(PointList.Count - 1);
               PointList.AddPointList(primVertices.PointList);

               //2. Build indices. Do not do it if poly curve contains poly line segments only.
               PointBase endPoint = PointList.Last();
               bool startEndPointsAreEqual = CoordsAreWithinVertexTol(PointList[0], endPoint);
               SegmentIndices segmentIndices = null;
               if (PolyLinesOnly)
               {
                  // Kind of workaround to ensure that the first and last points, if they are supposed to be 
                  // the same, are exactly the same.
                  if (startEndPointsAreEqual)
                     PointList[0] = endPoint;
               }
               else
               {
                  if (SegmentsIndices == null)
                     SegmentsIndices = new List<SegmentIndices>();

                  if (primVertices as PolyLineVertices != null)
                  {
                     segmentIndices = new PolyLineIndices(primVertices.PointList.Count - 1);
                  }
                  else if (primVertices as ArcVertices != null)
                  {
                     segmentIndices = new ArcIndices();
                  }
                  else
                     throw new ArgumentException("Unknown PrimVertices object");

                  segmentIndices.CalcIndices(SegmentsIndices.Count == 0 ? MinIndexValue : SegmentsIndices.Last().GetEnd());

                  // if start and end points of poly curve are equal we should remove duplicated point from vertices array.
                  // Indices array should be updated accordingly ( to access end point programmer should use start index).
                  if (startEndPointsAreEqual)
                  {
                     PointList.Points.RemoveAt(PointList.Count - 1);
                     segmentIndices.SetEnd(MinIndexValue);
                  }

                  //TryMerge is implemented for polylines because we can easily merge two consecutive polyline segments.
                  if (SegmentsIndices.Count == 0 || !SegmentsIndices.Last().TryMerge(segmentIndices))
                     SegmentsIndices.Add(segmentIndices);
               }
            }
         }

         public bool AddPrimVertices(PrimVertices primVertices)
         {
            if (primVertices == null)
               return false;

            //PrimVertices class guarantees that number of points is always >= 2.
            var curvePoints = primVertices.PointList.Points;
            var pointsCount = curvePoints.Count;

            PointBase currentStartPoint = PrimVerticesList.Count != 0 ? PrimVerticesList[0][0] : null;
            PointBase currentEndPoint = PrimVerticesList.Count != 0 ? PrimVerticesList.Last().Last() : null;
            bool addAtEnd = true;
            bool reverseCurve = false;

            if (currentStartPoint != null && currentEndPoint != null)
            {
               // Need to check all possible connections between the current curve and the
               // existing curve by checking start and endpoints.

               // For options to attach the next curve to the existing curve below.
               if (!CoordsAreWithinVertexTol(curvePoints[0], currentEndPoint))
               {
                  if (CoordsAreWithinVertexTol(curvePoints[pointsCount - 1], currentEndPoint))
                  {
                     reverseCurve = true;
                  }
                  else
                  {
                     addAtEnd = false;
                     if (CoordsAreWithinVertexTol(curvePoints[0], currentStartPoint))
                     {
                        reverseCurve = true;
                     }
                     else
                     {
                        //Neither start nor end point of Input curve coincide with any of this polycurve's edge points.
                        //In this case we do not know how to connect them, so return false.
                        return false;
                     }
                  }
               }
            }

            if (reverseCurve)
               curvePoints.Reverse();

            if (primVertices as PolyLineVertices == null)
               PolyLinesOnly = false;

            if (addAtEnd)
               PrimVerticesList.Add(primVertices);
            else
               PrimVerticesList.Insert(0, primVertices);

            return true;
         }
      }

      /// <summary>
      /// Creates a default plane whose origin is at (0, 0, 0) and normal is (0, 0, 1).
      /// </summary>
      /// <returns>
      /// The Plane.
      /// </returns>
      public static Plane CreateDefaultPlane()
      {
         return CreatePlaneByNormalAtOrigin(XYZ.BasisZ);
      }

      /// <summary>
      /// Creates a plane whose origin is at (0, 0, 0) and normal is zDir.
      /// </summary>
      /// <param name="zDir">The plane normal.</param>
      /// <returns>
      /// The Plane.
      /// </returns>
      public static Plane CreatePlaneByNormalAtOrigin(XYZ zDir)
      {
         if (zDir == null)
            return null;
         return Plane.CreateByNormalAndOrigin(zDir, XYZ.Zero);
      }

      /// <summary>
      /// Creates a plane by its X and Y directions whose origin is at (0, 0, 0).
      /// </summary>
      /// <param name="xDir">The plane X direction.</param>
      /// <param name="yDir">The plane Y direction.</param>
      /// <returns>
      /// The Plane.
      /// </returns>
      public static Plane CreatePlaneByXYVectorsAtOrigin(XYZ xDir, XYZ yDir)
      {
         if (xDir == null || yDir == null)
            return null;
         return CreatePlaneByXYVectorsContainingPoint(xDir, yDir, XYZ.Zero);
      }

      /// <summary>
      /// Try to create a Plane from a Transform whose origin is acceptable by Revit standards.
      /// </summary>
      /// <param name="lcs">The transform.  It may be null.</param>
      /// <returns>A valid Plane, or null if the input is null.</returns>
      /// <remarks>Note: this does not guarantee that the plane origin will be acceptable, if, for example,
      /// the closest point to the origin on the Plane is still farther away than the Revit limit.</remarks>
      public static Plane CreatePlaneFromTransformNearOrigin(Transform lcs)
      {
         if (lcs == null)
            return null;
         return CreatePlaneByXYVectorsContainingPoint(lcs.BasisX, lcs.BasisY, lcs.Origin);
      }

      /// <summary>
      /// Creates a plane whose normal is zDir and which contains the point point.
      /// </summary>
      /// <param name="xDir">The plane X direction.</param>
      /// <param name="yDir">The plane Y direction.</param>
      /// <param name="point">A point on the plane.</param>
      /// <returns>The Plane.</returns>
      /// <remarks>The origin of the plane will be the original point as long as it isn't "too far" from the origin.
      /// If it is greater than about 20 miles from the origin, the closest point to the global origin on the plane will be used.
      /// Note: this does not guarantee that the plane origin will be acceptable, if, for example,
      /// the closest point to the origin on the Plane is still farther away than the Revit internal limit.</remarks>
      public static Plane CreatePlaneByXYVectorsContainingPoint(XYZ xDir, XYZ yDir, XYZ point)
      {
         if (xDir == null || yDir == null || point == null)
            return null;
         XYZ zDir = xDir.CrossProduct(yDir);

         double distanceToOrigin = point.DistanceTo(XYZ.Zero);
         XYZ orig = (distanceToOrigin < 100000) ? point : zDir.DotProduct(point) * zDir;
         return Plane.Create(new Frame(orig, xDir, yDir, zDir));
      }

      /// <summary>
      /// Create a Transform from an X vector, a Y vector, a Z vector, and an origin.
      /// </summary>
      /// <param name="xVec">The X vector.</param>
      /// <param name="yVec">The Y vector.</param>
      /// <param name="zVec">The Z vector.</param>
      /// <param name="origin">The origin.</param>
      /// <returns>The Transform.</returns>
      public static Transform CreateTransformFromVectorsAndOrigin(XYZ xVec, XYZ yVec, XYZ zVec, XYZ origin)
      {
         if (xVec == null || yVec == null || zVec == null || origin == null)
            return null;
         Transform transform = Transform.CreateTranslation(origin);
         transform.BasisX = xVec;
         transform.BasisY = yVec;
         transform.BasisZ = zVec;
         return transform;
      }

      /// <summary>
      /// Create a Transform from a Plane.
      /// </summary>
      /// <param name="plane">The plane.</param>
      /// <returns>A Transform with the same X, Y, Z vectors and origin as the input plane.</returns>
      public static Transform CreateTransformFromPlane(Plane plane)
      {
         if (plane == null)
            return null;
         Transform transform = Transform.CreateTranslation(plane.Origin);
         transform.BasisX = plane.XVec;
         transform.BasisY = plane.YVec;
         transform.BasisZ = plane.Normal;
         return transform;
      }

      /// <summary>
      /// Create a Transform from a PlanarFace.
      /// </summary>
      /// <param name="face">The PlanarFace.</param>
      /// <returns>A Transform with the same X, Y, Z vectors and origin as the input face.</returns>
      public static Transform CreateTransformFromPlanarFace(PlanarFace face)
      {
         if (face == null)
            return null;
         Transform transform = Transform.CreateTranslation(face.Origin);
         transform.BasisX = face.XVector;
         transform.BasisY = face.YVector;
         transform.BasisZ = face.FaceNormal;
         return transform;
      }

      /// <summary>
      /// Checks if curve is flipped relative to a local coordinate system.
      /// </summary>
      /// <param name="lcs">The local coordinate system.</param>
      /// <param name="curve">The curve.</param>
      /// <returns>True if the curve is flipped to the plane, false otherwise.</returns>
      public static bool MustFlipCurve(Transform lcs, Curve curve)
      {
         XYZ xVector = null;
         XYZ yVector = null;
         if (curve is Arc)
         {
            Arc arc = curve as Arc;
            xVector = arc.XDirection;
            yVector = arc.YDirection;
         }
         else if (curve is Ellipse)
         {
            Ellipse ellipse = curve as Ellipse;
            xVector = ellipse.XDirection;
            yVector = ellipse.YDirection;
         }
         else
            return false;

         UV realListX = ConvertVectorToLocalCoordinates(lcs, xVector);
         UV realListY = ConvertVectorToLocalCoordinates(lcs, yVector);

         double dot = realListY.U * (-realListX.V) + realListY.V * (realListX.U);
         if (dot < -MathUtil.Eps())
            return true;

         return false;
      }

      /// <summary>
      /// Calculates the slope of an extrusion relative to an axis.
      /// </summary>
      /// <param name="extrusionDirection">The extrusion direction.</param>
      /// <param name="axis">The axis.</param>
      /// <returns>The slope.</returns>
      /// <remarks>This is a simple routine mainly intended for beams and columns.</remarks>
      static public double GetSimpleExtrusionSlope(XYZ extrusionDirection, IFCExtrusionBasis axis)
      {
         double zOff = (axis == IFCExtrusionBasis.BasisZ) ? (1.0 - Math.Abs(extrusionDirection[2])) : Math.Abs(extrusionDirection[2]);
         double scaledAngle = UnitUtil.ScaleAngle(MathUtil.SafeAsin(zOff));
         return scaledAngle;
      }

      /// <summary>
      /// Converts vector from global coordinates (X,Y,Z) to local coordinates (U,V).
      /// </summary>
      /// <param name="lcs">The local coordinate system.  If not supplied, assumed to be identity.</param>
      /// <param name="vector">The vector in global coordinates.</param>
      /// <returns>The converted values.</returns>
      public static UV ConvertVectorToLocalCoordinates(Transform lcs, XYZ vector)
      {
         if (lcs != null)
         {
            return new UV(vector.DotProduct(lcs.BasisX), vector.DotProduct(lcs.BasisY));
         }

         return new UV(vector.X, vector.Y);
      }

      /// <summary>
      /// Obtains a new curve transformed via the indicated translation vector. 
      /// </summary>
      /// <param name="originalCurve">The curve.</param>
      /// <param name="translationVector">The translation vector.</param>
      /// <returns>The new translated curve.</returns>
      public static Curve MoveCurve(Curve originalCurve, XYZ translationVector)
      {
         Transform moveTrf = Transform.CreateTranslation(translationVector);
         return originalCurve.CreateTransformed(moveTrf);
      }

      /// <summary>
      /// Obtains a new CurveLoop transformed via the indicated translation vector. 
      /// </summary>
      /// <param name="originalCurveLoop">The curve loop.</param>
      /// <param name="translationVector">The translation vector.</param>
      /// <returns>The new translated curve loop.</returns>
      public static CurveLoop MoveCurveLoop(CurveLoop originalCurveLoop, XYZ translationVector)
      {
         Transform moveTrf = Transform.CreateTranslation(translationVector);
         CurveLoop newCurveLoop = new CurveLoop();
         foreach (Curve curve in originalCurveLoop)
            newCurveLoop.Append(MoveCurve(curve, translationVector));
         return newCurveLoop;
      }

      /// <summary>
      /// Obtains a new CurveLoop transformed via the indicated transform.
      /// </summary>
      /// <param name="originalCurveLoop">The curve loop.</param>
      /// <param name="trf">The transform.</param>
      /// <returns>The new transformed curve loop.</returns>
      public static CurveLoop TransformCurveLoop(CurveLoop originalCurveLoop, Transform trf)
      {
         CurveLoop newCurveLoop = new CurveLoop();
         foreach (Curve curve in originalCurveLoop)
            newCurveLoop.Append(curve.CreateTransformed(trf));
         return newCurveLoop;
      }

      /// <summary>
      /// Checks if curve is line or arc.
      /// </summary>
      /// <param name="curve">
      /// The curve.
      /// </param>
      /// <returns>
      /// True if the curve is line or arc, false otherwise.
      /// </returns>
      public static bool CurveIsLineOrArc(Curve curve)
      {
         return curve is Line || curve is Arc;
      }

      /// <summary>
      /// Reverses curve loop.
      /// </summary>
      /// <param name="curveloop">The curveloop.</param>
      /// <returns>The reversed curve loop.</returns>
      public static CurveLoop ReverseOrientation(CurveLoop curveloop)
      {
         CurveLoop copyOfCurveLoop = CurveLoop.CreateViaCopy(curveloop);
         copyOfCurveLoop.Flip();
         return copyOfCurveLoop;
      }

      /// <summary>
      /// Gets origin, X direction and curve bound from a curve.
      /// </summary>
      /// <param name="curve">The curve.</param>
      /// <param name="curveBounds">The output curve bounds.</param>
      /// <param name="xDirection">The output X direction.</param>
      /// <param name="origin">The output origin.</param>
      public static void GetAxisAndRangeFromCurve(Curve curve,
         out IFCRange curveBounds, out XYZ xDirection, out XYZ origin)
      {
         curveBounds = new IFCRange(curve.GetEndParameter(0), curve.GetEndParameter(1));
         origin = curve.Evaluate(curveBounds.Start, false);
         if (curve is Arc)
         {
            Arc arc = curve as Arc;
            xDirection = arc.XDirection;
         }
         else if (curve is Ellipse)
         {
            Ellipse ellipse = curve as Ellipse;
            xDirection = ellipse.XDirection;
         }
         else
         {
            Transform trf = curve.ComputeDerivatives(curveBounds.Start, false);
            xDirection = trf.get_Basis(0);
         }
      }

      /// <summary>
      /// Creates and returns an instance of the Options class with current view's DetailLevel or the detail level set to Fine if current view is not checked.
      /// </summary>
      public static Options GetIFCExportGeometryOptions()
      {
         Options options = new Options();
         options.DetailLevel =
            ExporterCacheManager.ExportOptionsCache.FilterViewForExport?.DetailLevel ??
            ViewDetailLevel.Fine;
         return options;
      }

      /// <summary>
      /// Collects all solids and meshes within a GeometryElement.
      /// </summary>
      /// <remarks>
      /// Added in 2013 to replace the temporary API method ExporterIFCUtils.GetSolidMeshGeometry.
      /// </remarks>
      /// <param name="geomElemToUse">The GeometryElement.</param>
      /// <param name="trf">The initial Transform applied on the GeometryElement.</param>
      /// <returns>The collection of solids and meshes.</returns>
      public static SolidMeshGeometryInfo GetSolidMeshGeometry(GeometryElement geomElemToUse, Transform trf)
      {
         SolidMeshGeometryInfo geometryInfo = new SolidMeshGeometryInfo();
         if (geomElemToUse != null)
         {
            // call to recursive helper method to obtain all solid and mesh geometry within geomElemToUse
            geometryInfo.CollectSolidMeshGeometry(geomElemToUse, ExporterCacheManager.AllocatedGeometryObjectCache);
         }
         return geometryInfo;
      }

      /// <summary>
      /// Collects all solids and meshes within a GeometryElement, given the input the Element
      /// </summary>
      /// <param name="element">the Element</param>
      /// <returns>SolidMeshGeometryInfo</returns>
      public static SolidMeshGeometryInfo GetSolidMeshGeometry(Element element)
      {
         Options options = GetIFCExportGeometryOptions();
         GeometryElement geomElem = element.get_Geometry(options);
         return GetSolidMeshGeometry(geomElem, Transform.Identity);
      }

      /// <summary>
      /// Collects all meshes within a GeometryElement and all solids clipped between a given IFCRange.
      /// </summary>
      /// <remarks>
      /// Added in 2013 to replace the temporary API method ExporterIFCUtils.GetClippedSolidMeshGeometry.
      /// </remarks>
      /// <param name="elem">
      /// The Element from which we can obtain a bounding box. Not handled directly in this method, it is used in an internal helper method.
      /// </param>
      /// <param name="geomElemToUse">
      /// The GeometryElement.
      /// </param>
      /// <param name="range">
      /// The upper and lower levels which act as the clipping boundaries.
      /// </param>
      /// <returns>The collection of solids and meshes.</returns>
      public static SolidMeshGeometryInfo GetClippedSolidMeshGeometry(GeometryElement geomElemToUse, IFCRange range)
      {
         SolidMeshGeometryInfo geometryInfo = GetSolidMeshGeometry(geomElemToUse, Transform.Identity);
         geometryInfo.ClipSolidsList(geomElemToUse, range);
         return geometryInfo;
      }

      /// <summary>
      /// Collects all solids and meshes within a GeometryElement; the solids which consist of multiple closed volumes
      /// will be split into single closed volume Solids.
      /// </summary>
      /// <remarks>
      /// Added in 2013 to replace the temporary API method ExporterIFCUtils.GetSplitSolidMeshGeometry.
      /// </remarks>
      /// <param name="geomElemToUse">The GeometryElement.</param>
      /// <param name="trf">The transform.</param>
      /// <returns>The collection of solids and meshes.</returns>
      public static SolidMeshGeometryInfo GetSplitSolidMeshGeometry(GeometryElement geomElemToUse, Transform trf)
      {
         SolidMeshGeometryInfo geometryInfo = GetSolidMeshGeometry(geomElemToUse, Transform.Identity);
         SplitSolids(geometryInfo);
         return geometryInfo;
      }

      /// <summary>
      /// Collects all solids and meshes within a GeometryElement; the solids which consist of multiple closed volumes
      /// will be split into single closed volume Solids.
      /// </summary>
      /// <remarks>
      /// Added in 2013 to replace the temporary API method ExporterIFCUtils.GetSplitSolidMeshGeometry.
      /// </remarks>
      /// <param name="geomElemToUse">The GeometryElement.</param>
      /// <returns>The collection of solids and meshes.</returns>
      public static SolidMeshGeometryInfo GetSplitSolidMeshGeometry(GeometryElement geomElemToUse)
      {
         return GetSplitSolidMeshGeometry(geomElemToUse, Transform.Identity);
      }

      /// <summary>
      /// Collects all solids and meshes within a GeometryElement; the solids which consist of multiple closed volumes
      /// will be split into single closed volume Solids.
      /// </summary>
      /// <remarks>
      /// Added in 2013 to replace the temporary API method ExporterIFCUtils.GetSplitClippedSolidMeshGeometry.
      /// </remarks>
      /// <param name="range">
      /// The upper and lower levels which act as the clipping boundaries.
      /// </param>
      /// <param name="geomElemToUse">The GeometryElement.</param>
      /// <returns>The collection of solids and meshes.</returns>
      public static SolidMeshGeometryInfo GetSplitClippedSolidMeshGeometry(GeometryElement geomElemToUse, IFCRange range)
      {
         if (range == null)
            return GetSplitSolidMeshGeometry(geomElemToUse);

         SolidMeshGeometryInfo geometryInfo = GetClippedSolidMeshGeometry(geomElemToUse, range);
         SplitSolids(geometryInfo);
         return geometryInfo;
      }

      /// <summary>
      /// The maximum number of faces in a Solid before we decide not to split it.
      /// Larger than this can cause sigificant performance issues.
      /// </summary>
      /// <remarks>
      /// Internal tests show perfectly good behavior at 1044 faces, so setting
      /// this value based on that.  This may be tweaked over time, or other
      /// methods used instead.
      /// </remarks>
      public static int MaxFaceCountForSplitVolumes = 2048;

      /// <summary>
      /// Splits a Solid into distinct volumes.
      /// </summary>
      /// <param name="solid">The initial solid.</param>
      /// <returns>The list of volumes.</returns>
      /// <remarks>This calls the internal SolidUtils.SplitVolumes routine, but does additional cleanup work to properly dispose of stale data.</remarks>
      public static IList<Solid> SplitVolumes(Solid solid)
      {
         IList<Solid> splitVolumes = null;
         try
         {
            if (solid.Faces.Size < GeometryUtil.MaxFaceCountForSplitVolumes)
            {
               splitVolumes = SolidUtils.SplitVolumes(solid);

               // Fall back to exporting just the original Solid if we got any Solids without volume
               if (splitVolumes.Any(x => x.Volume < 0.0 || MathUtil.IsAlmostEqual(x.Volume, 0.0)))
                  throw new InvalidOperationException();

               foreach (Solid currSolid in splitVolumes)
               {
                  // The geometry element created by SplitVolumes is a copy which will have its own allocated
                  // membership - this needs to be stored and disposed of (see AllocatedGeometryObjectCache
                  // for details)
                  ExporterCacheManager.AllocatedGeometryObjectCache.AddGeometryObject(currSolid);
               }
            }
         }
         catch
         {
            splitVolumes = null;
         }

         if (splitVolumes == null)
         {
            // Split volumes can fail; in this case, we'll export the original solid.
            splitVolumes = new List<Solid>() { solid };
         }

         return splitVolumes;
      }

      /// <summary>
      /// Splits any solid volumes which consist of multiple closed bodies into individual solids (and updates the storage accordingly).
      /// </summary>
      public static void SplitSolids(SolidMeshGeometryInfo info)
      {
         IList<SolidInfo> splitSolidsList = new List<SolidInfo>();

         foreach (SolidInfo solidInfo in info.SolidInfoList)
         {
            Element element = solidInfo.OwnerElement;
            IList<Solid> splitSolids = GeometryUtil.SplitVolumes(solidInfo.Solid);
            foreach (Solid splitSolid in splitSolids)
            {
               splitSolidsList.Add(new SolidInfo(splitSolid, element));
            }
         }

         info.SolidInfoList = splitSolidsList;
      }

      /// <summary>
      /// Indicates whether or not the loop has the same sense when used to bound the face as when first defined.
      /// </summary>
      /// <param name="boundary">The boundary.</param>
      /// <returns>If false the senses of all its component oriented edges are implicitly reversed when used in the face.</returns>
      public static bool BoundaryHasSameSense(IFCAnyHandle boundary)
      {
         bool? hasSameSense = IFCAnyHandleUtil.GetBooleanAttribute(boundary, "Orientation");
         return hasSameSense.HasValue && hasSameSense.Value;
      }

      /// <summary>
      /// Gets the boundary polygon for a given boundary.
      /// </summary>
      /// <param name="boundary">The boundary.</param>
      /// <returns>The boundary curves for the polygon.</returns>
      public static List<IFCAnyHandle> GetBoundaryPolygon(IFCAnyHandle boundary)
      {
         IFCAnyHandle bound = IFCAnyHandleUtil.GetInstanceAttribute(boundary, "Bound");

         return IFCAnyHandleUtil.GetAggregateInstanceAttribute<List<IFCAnyHandle>>(bound, "Polygon");
      }

      /// <summary>
      /// Gets the boundary handles for a given face.
      /// </summary>
      /// <param name="face">The face.</param>
      /// <returns>The boundary handles.</returns>
      public static HashSet<IFCAnyHandle> GetFaceBounds(IFCAnyHandle face)
      {
         return IFCAnyHandleUtil.GetAggregateInstanceAttribute<HashSet<IFCAnyHandle>>(face, "Bounds");
      }

      /// <summary>
      /// Gets the IfcObjectPlacement handle stored as the reference for an IfcLocalPlacement.
      /// </summary>
      /// <param name="localPlacement"></param>
      /// <returns>The IfcObjectPlacement handle.  Return can be a handle without a value, if there is no value set in the IfcLocalPlacement.</returns>
      public static IFCAnyHandle GetPlacementRelToFromLocalPlacement(IFCAnyHandle localPlacement)
      {
         return IFCAnyHandleUtil.GetInstanceAttribute(localPlacement, "PlacementRelTo");
      }

      /// <summary>
      ///  Gets the IfcAxis2Placement handle stored as the relative placement for an IfcLocalPlacement.
      /// </summary>
      /// <param name="localPlacement"> The IfcLocalPlacement handle.</param>
      /// <returns>The IfcAxis2Placement handle.</returns>
      public static IFCAnyHandle GetRelativePlacementFromLocalPlacement(IFCAnyHandle localPlacement)
      {
         return IFCAnyHandleUtil.GetInstanceAttribute(localPlacement, "RelativePlacement");
      }

      /// <summary>
      /// Gets the collection of IfcRepresentationMaps stored in an IfcTypeProduct handle.
      /// </summary>
      /// <param name="typeProduct">The IfcTypeProduct handle.</param>
      /// <returns>The representation maps.</returns>
      public static List<IFCAnyHandle> GetRepresentationMaps(IFCAnyHandle typeProduct)
      {
         return IFCAnyHandleUtil.GetAggregateInstanceAttribute<List<IFCAnyHandle>>(typeProduct, "RepresentationMaps");
      }

      /// <summary>
      /// Adds items to a given shape handle.
      /// </summary>
      /// <param name="shape">The shape handle.</param>
      /// <param name="items">The items.</param>
      public static void AddItemsToShape(IFCAnyHandle shape, ISet<IFCAnyHandle> items)
      {
         HashSet<IFCAnyHandle> repItemSet = IFCAnyHandleUtil.GetAggregateInstanceAttribute<HashSet<IFCAnyHandle>>(shape, "Items");
         foreach (IFCAnyHandle repItem in items)
         {
            repItemSet.Add(repItem);
         }
         IFCAnyHandleUtil.SetAttribute(shape, "Items", repItemSet);
      }

      /// <summary>
      /// Sets the IfcAxis2Placement handle stored as the placement relative to for an IfcLocalPlacement.
      /// </summary>
      /// <param name="localPlacement">The IfcLocalPlacement handle.</param>
      /// <param name="newPlacementRelTo">The IfcObjectPlacement handle to use as the placement relative to.</param>
      public static void SetPlacementRelTo(IFCAnyHandle localPlacement, IFCAnyHandle newPlacementRelTo)
      {
         IFCAnyHandleUtil.SetAttribute(localPlacement, "PlacementRelTo", newPlacementRelTo);
      }

      /// <summary>
      /// Sets the IfcAxis2Placement handle stored as the relative placement for an IfcLocalPlacement.
      /// </summary>
      /// <param name="localPlacement">The IfcLocalPlacement handle.</param>
      /// <param name="newRelativePlacement">The IfcAxis2Placement handle to use as the relative placement.</param>
      public static void SetRelativePlacement(IFCAnyHandle localPlacement, IFCAnyHandle newRelativePlacement)
      {
         IFCAnyHandleUtil.SetAttribute(localPlacement, "RelativePlacement", newRelativePlacement);
      }

      /// <summary>
      /// Sets the IfcAxis2Placement handle stored as the placement relative to for an IfcLocalPlacement.
      /// </summary>
      /// <param name="localPlacement">The IfcLocalPlacement handle.</param>
      /// <param name="newRelativePlacement">The IfcAxis2Placement handle to use as the relative placement.</param>
      /// <param name="newPlacementRelTo">The IfcObjectPlacement handle to use as the placement relative to.</param>
      public static void UpdateLocalPlacement(IFCAnyHandle localPlacement, IFCAnyHandle newPlacementRelTo, IFCAnyHandle newRelativePlacement)
      {
         SetPlacementRelTo(localPlacement, newPlacementRelTo);
         SetRelativePlacement(localPlacement, newRelativePlacement);
      }

      /// <summary>
      /// Get geometry of one level of a potentially multi-story stair, ramp, or railing.
      /// </summary>
      /// <param name="geomElement">The original geometry.</param>
      /// <param name="numFlights">The number of stair flights, or 0 if unknown.  If there is exactly 1 flight, return the original geoemtry.</param>
      /// <returns>The geometry element with its symbol id.</returns>
      /// <remarks>This routine may not work properly for railings created before 2006.  If you get
      /// poor representations from such railings, please upgrade the railings if possible.</remarks>
      public static (GeometryElement element, ElementId symbolId) GetOneLevelGeometryElement(GeometryElement geomElement, int numFlights)
      {
         if (geomElement == null)
            return (null, null);

         if (numFlights == 1)
            return (geomElement, null);

         foreach (GeometryObject geomObject in geomElement)
         {
            if (!(geomObject is GeometryInstance))
               continue;
            GeometryInstance geomInstance = geomObject as GeometryInstance;
            if (!MathUtil.IsAlmostZero(geomInstance.Transform.Origin.Z))
               continue;
            Element baseSymbol = geomInstance.GetDocument()?.GetElement(geomInstance.GetSymbolGeometryId().SymbolId);
            if (!(baseSymbol is ElementType))
               continue;
            GeometryElement symbolGeomElement = geomInstance.GetSymbolGeometry();

            // For railings created before 2006, the GeometryElement could be null.  In this case, we will use
            // a more general technique of getting geometry below, which will unfortanately result in worse
            // representations.  If this is a concern, please upgrade the railings to any format since 2006.
            if (symbolGeomElement != null)
            {
               ElementId oneLevelGeomSymbolId = geomInstance.GetSymbolGeometryId().SymbolId;
               Transform trf = geomInstance.Transform;
               if (trf != null && !trf.IsIdentity)
                  return (symbolGeomElement.GetTransformed(trf), oneLevelGeomSymbolId);
               else
                  return (symbolGeomElement, oneLevelGeomSymbolId);
            }
         }
         return (geomElement, null);
      }

      /// <summary>
      /// Get additional geometry of one level of a potentially multi-story stair, ramp, or railing that wasn't added in GetOneLevelGeometryElement
      /// </summary>
      /// <param name="allLevelsGeometry">The original geometry.</param>
      /// <param name="mainGeometrySymbolId">The symbol id of the level main geometry.</param>
      /// <returns>The geometry elements list.</returns>
      public static List<GeometryElement> GetAdditionalOneLevelGeometry(GeometryElement allLevelsGeometry, ElementId mainGeometrySymbolId)
      {
         List<GeometryElement> additionalGeometry = new List<GeometryElement>();

         if (allLevelsGeometry == null || mainGeometrySymbolId == null)
            return additionalGeometry;

         // Collect geometry and its Origin.Z grouped by symbols
         Dictionary<ElementId, IList<Tuple<GeometryInstance, double>>> symbols = new Dictionary<ElementId, IList<Tuple<GeometryInstance, double>>>();
         foreach (GeometryObject geomObject in allLevelsGeometry)
         {
            GeometryInstance instance = geomObject as GeometryInstance;
            if (instance == null || instance.GetSymbolGeometryId() == null)
               continue;

            ElementId id = instance.GetSymbolGeometryId().SymbolId;

            IList<Tuple<GeometryInstance, double>> geomInstances;
            if (!symbols.TryGetValue(id, out geomInstances))
            {
               geomInstances = new List<Tuple<GeometryInstance, double>>();
               symbols[id] = geomInstances;
            }
            geomInstances.Add(new Tuple<GeometryInstance, double>(instance, instance.Transform.Origin.Z));
         }

         // Define the number of flights as number of main instances
         int numFlights = 0;
         IList<Tuple<GeometryInstance, double>> instances;
         if (symbols.TryGetValue(mainGeometrySymbolId, out instances))
            numFlights = instances.Count;

         if (numFlights < 1)
            return additionalGeometry;

         // Collect proper amount of instances of each geometry
         List<GeometryInstance> instncesToAdd = new List<GeometryInstance>();
         foreach (KeyValuePair<ElementId, IList<Tuple<GeometryInstance, double>>> symbol in symbols)
         {
            if (symbol.Key == mainGeometrySymbolId)
               continue;

            int numCurrInstances = symbol.Value.Count;
            if (numCurrInstances == 0 || numCurrInstances % numFlights != 0)
               continue;

            // We take 'numCurrInstances/numFlights' instances with the lowest Origin.Z
            // The oter instances are repeateble geometry of another level(flight)
            int numInstancesToAdd = symbol.Value.Count / numFlights;
            List<Tuple<GeometryInstance, double>> currInstances = symbol.Value.ToList();
            instncesToAdd.AddRange(symbol.Value.OrderBy(x => x.Item2).Select(x => x.Item1).Take(numInstancesToAdd).ToList());
         }

         // Collect output geometry
         foreach (GeometryInstance instance in instncesToAdd)
         {
            Element baseSymbol = instance.GetDocument()?.GetElement(instance.GetSymbolGeometryId().SymbolId);
            if (!(baseSymbol is ElementType))
               continue;
            GeometryElement symbolGeomElement = instance.GetSymbolGeometry();
            if (symbolGeomElement != null)
            {
               Transform trf = instance.Transform;
               if (trf != null && !trf.IsIdentity)
                  additionalGeometry.Add(symbolGeomElement.GetTransformed(trf));
               else
                  additionalGeometry.Add(symbolGeomElement);
            }
         }
         return additionalGeometry;
      }


      /// <summary>
      /// Projects a point to the closest point on the XY plane of a local coordinate system.
      /// </summary>
      /// <param name="lcs">The local coordinate system.</param>
      /// <param name="point">The point.</param>
      /// <returns>The UV of the projected point.</returns>
      public static UV ProjectPointToXYPlaneOfLCS(Transform lcs, XYZ point)
      {
         if (lcs == null || point == null)
            return null;

         XYZ diff = (point - lcs.Origin);
         return new UV(diff.DotProduct(lcs.BasisX), diff.DotProduct(lcs.BasisY));
      }

      /// <summary>
      /// Generates the UV value of a point projected to the XY plane of a transform representing a local coordinate system, given an extrusion direction.
      /// </summary>
      /// <param name="lcs">The local coordinate system.</param>
      /// <param name="projDir">The projection direction.</param>
      /// <param name="point">The point.</param>
      /// <returns>The UV value.</returns>
      public static UV ProjectPointToXYPlaneOfLCS(Transform lcs, XYZ projDir, XYZ point)
      {
         XYZ zDir = lcs.BasisZ;

         double denom = projDir.DotProduct(zDir);
         if (MathUtil.IsAlmostZero(denom))
            return new UV(point.X, point.Y);

         XYZ xDir = lcs.BasisX;
         XYZ yDir = lcs.BasisY;
         XYZ orig = lcs.Origin;

         double distToPlane = ((orig - point).DotProduct(zDir)) / denom;
         XYZ pointProj = distToPlane * projDir + point;
         XYZ pointProjOffset = pointProj - orig;
         UV pointProjUV = new UV(pointProjOffset.DotProduct(xDir), pointProjOffset.DotProduct(yDir));
         return pointProjUV;
      }

      /// <summary>
      /// Specifies the types of curves found in a boundary curve loop.
      /// </summary>
      public enum FaceBoundaryType
      {
         Polygonal,  // all curves are line segments.
         LinesAndArcs, // all curves are line segments or arcs.
         Complex // some curves are neither line segments nor arcs.
      }

      private static CurveLoop GetFaceBoundary(Face face, EdgeArray faceBoundary, XYZ baseLoopOffset,
          bool polygonalOnly, out FaceBoundaryType faceBoundaryType)
      {
         faceBoundaryType = FaceBoundaryType.Polygonal;
         CurveLoop currLoop = new CurveLoop();
         foreach (Edge faceBoundaryEdge in faceBoundary)
         {
            Curve edgeCurve = faceBoundaryEdge.AsCurveFollowingFace(face);
            Curve offsetCurve = (baseLoopOffset != null) ? MoveCurve(edgeCurve, baseLoopOffset) : edgeCurve;
            if (!(offsetCurve is Line))
            {
               if (polygonalOnly)
               {
                  IList<XYZ> tessPts = offsetCurve.Tessellate();
                  int numTessPts = tessPts.Count;
                  for (int ii = 0; ii < numTessPts - 1; ii++)
                  {
                     Line line = Line.CreateBound(tessPts[ii], tessPts[ii + 1]);
                     currLoop.Append(line);
                  }
               }
               else
               {
                  currLoop.Append(offsetCurve);
               }

               if (offsetCurve is Arc)
                  faceBoundaryType = FaceBoundaryType.LinesAndArcs;
               else
                  faceBoundaryType = FaceBoundaryType.Complex;
            }
            else
               currLoop.Append(offsetCurve);
         }
         return currLoop;
      }

      /// <summary>
      /// Gets the outer and inner boundaries of a Face as CurveLoops.
      /// </summary>
      /// <param name="face">The face.</param>
      /// <param name="baseLoopOffset">The amount to translate the origin of the face plane.  This is used if the start of the extrusion
      /// is offset from the base face.  The argument is null otherwise.</param>
      /// <param name="faceBoundaryTypes">Returns whether the boundaries consist of lines only, lines and arcs, or complex curves.</param>
      /// <returns>1 outer and 0 or more inner curve loops corresponding to the face boundaries.</returns>
      public static IList<CurveLoop> GetFaceBoundaries(Face face, XYZ baseLoopOffset, out IList<FaceBoundaryType> faceBoundaryTypes)
      {
         faceBoundaryTypes = new List<FaceBoundaryType>();

         EdgeArrayArray faceBoundaries = face.EdgeLoops;
         IList<CurveLoop> extrusionBoundaryLoops = new List<CurveLoop>();
         foreach (EdgeArray faceBoundary in faceBoundaries)
         {
            FaceBoundaryType currFaceBoundaryType;
            CurveLoop currLoop = GetFaceBoundary(face, faceBoundary, baseLoopOffset, false, out currFaceBoundaryType);
            faceBoundaryTypes.Add(currFaceBoundaryType);
            extrusionBoundaryLoops.Add(currLoop);
         }
         return extrusionBoundaryLoops;
      }

      private static CurveLoop GetOuterFaceBoundary(Face face, XYZ baseLoopOffset, bool polygonalOnly, out FaceBoundaryType faceBoundaryType)
      {
         faceBoundaryType = FaceBoundaryType.Polygonal;

         EdgeArrayArray faceBoundaries = face.EdgeLoops;
         foreach (EdgeArray faceOuterBoundary in faceBoundaries)
            return GetFaceBoundary(face, faceOuterBoundary, baseLoopOffset, polygonalOnly, out faceBoundaryType);
         return null;
      }

      /// <summary>
      /// Group the extra faces in the extrusion by element id, representing clippings, recesses, and openings.
      /// </summary>
      /// <param name="elem">The element generating the base extrusion.</param>
      /// <param name="analyzer">The extrusion analyzer.</param>
      /// <returns>A list of connected faces for each element id that cuts the extrusion</returns>
      public static IDictionary<ElementId, ICollection<ICollection<Face>>> GetCuttingElementFaces(Element elem, ExtrusionAnalyzer analyzer)
      {
         IDictionary<ElementId, ICollection<Face>> cuttingElementFaces = new Dictionary<ElementId, ICollection<Face>>();

         IDictionary<Face, ExtrusionAnalyzerFaceAlignment> allFaces = analyzer.CalculateFaceAlignment();
         foreach (KeyValuePair<Face, ExtrusionAnalyzerFaceAlignment> currFace in allFaces)
         {
            if (currFace.Value == ExtrusionAnalyzerFaceAlignment.FullyAligned)
               continue;

            EdgeArrayArray faceEdges = currFace.Key.EdgeLoops;
            int numBoundaries = faceEdges.Size;
            if (numBoundaries == 0)
               continue;

            // In some cases the native function throws an exception, skip this face if it occurs
            ICollection<ElementId> generatingElementIds;
            try
            {
               generatingElementIds = elem.GetGeneratingElementIds(currFace.Key);
            }
            catch
            {
               throw new ArgumentException(string.Format("Can't get generating element Ids for face: {0}", currFace.Key.Id));
            }

            if (generatingElementIds == null)
               continue;

            foreach (ElementId generatingElementId in generatingElementIds)
            {
               ICollection<Face> elementFaces;

               if (cuttingElementFaces.ContainsKey(generatingElementId))
               {
                  elementFaces = cuttingElementFaces[generatingElementId];
               }
               else
               {
                  elementFaces = new HashSet<Face>();
                  cuttingElementFaces[generatingElementId] = elementFaces;
               }

               elementFaces.Add(currFace.Key);
            }
         }

         IDictionary<ElementId, ICollection<ICollection<Face>>> cuttingElementFaceCollections =
             new Dictionary<ElementId, ICollection<ICollection<Face>>>();
         foreach (KeyValuePair<ElementId, ICollection<Face>> cuttingElementFaceCollection in cuttingElementFaces)
         {
            ICollection<ICollection<Face>> faceCollections = new List<ICollection<Face>>();
            // Split into separate collections based on connectivity.
            while (cuttingElementFaceCollection.Value.Count > 0)
            {
               IList<Face> currCollection = new List<Face>();
               IEnumerator<Face> cuttingElementFaceCollectionEnumerator = cuttingElementFaceCollection.Value.GetEnumerator();
               cuttingElementFaceCollectionEnumerator.MoveNext();
               Face currFace = cuttingElementFaceCollectionEnumerator.Current;
               currCollection.Add(currFace);
               cuttingElementFaceCollection.Value.Remove(currFace);

               IList<Face> facesToProcess = new List<Face>();
               facesToProcess.Add(currFace);

               if (cuttingElementFaceCollection.Value.Count > 0)
               {
                  while (facesToProcess.Count > 0)
                  {
                     currFace = facesToProcess[0];
                     EdgeArray faceOuterBoundary = currFace.EdgeLoops.get_Item(0);

                     foreach (Edge edge in faceOuterBoundary)
                     {
                        Face adjoiningFace = edge.GetFace(1);
                        if (adjoiningFace.Equals(currFace))
                           adjoiningFace = edge.GetFace(0);

                        if (cuttingElementFaceCollection.Value.Contains(adjoiningFace))
                        {
                           currCollection.Add(adjoiningFace);
                           cuttingElementFaceCollection.Value.Remove(adjoiningFace);
                           facesToProcess.Add(adjoiningFace);
                        }
                     }

                     facesToProcess.Remove(facesToProcess[0]);
                  }
               }

               faceCollections.Add(currCollection);
            }

            cuttingElementFaceCollections[cuttingElementFaceCollection.Key] = faceCollections;
         }

         return cuttingElementFaceCollections;
      }

      private static IFCRange GetExtrusionRangeOfCurveLoop(CurveLoop loop, XYZ extrusionDirection)
      {
         IFCRange range = new IFCRange();
         bool init = false;
         foreach (Curve curve in loop)
         {
            if (!init)
            {
               if (curve.IsBound)
               {
                  IList<XYZ> coords = curve.Tessellate();
                  foreach (XYZ coord in coords)
                  {
                     double val = coord.DotProduct(extrusionDirection);
                     if (!init)
                     {
                        range.Start = val;
                        range.End = val;
                        init = true;
                     }
                     else
                     {
                        range.Start = Math.Min(range.Start, val);
                        range.End = Math.Max(range.End, val);
                     }
                  }
               }
               else
               {
                  double val = curve.GetEndPoint(0).DotProduct(extrusionDirection);
                  range.Start = val;
                  range.End = val;
                  init = true;
               }
            }
            else
            {
               double val = curve.GetEndPoint(0).DotProduct(extrusionDirection);
               range.Start = Math.Min(range.Start, val);
               range.End = Math.Max(range.End, val);
            }
         }
         return range;
      }

      private static bool IsInRange(IFCRange range, Transform lcs, XYZ extrusionDirection, out bool clipCompletely)
      {
         clipCompletely = false;
         if (range != null)
         {
            // This check is only applicable for cuts that are perpendicular to the extrusion direction.
            // For cuts that aren't, we can't easily tell if this cut is extraneous or not.
            if (!MathUtil.IsAlmostEqual(Math.Abs(lcs.BasisZ.DotProduct(extrusionDirection)), 1.0))
               return true;

            double eps = MathUtil.Eps();

            double parameterValue = lcs.Origin.DotProduct(extrusionDirection);

            if (range.Start > parameterValue - eps)
            {
               clipCompletely = true;
               return false;
            }
            if (range.End < parameterValue + eps)
               return false;
         }

         return true;
      }

      private static IList<UV> TransformAndProjectCurveLoopToPlane(ExporterIFC exporterIFC, CurveLoop loop, Transform lcs)
      {
         IList<UV> uvs = new List<UV>();

         XYZ projDir = lcs.BasisZ;
         foreach (Curve curve in loop)
         {
            XYZ point = curve.GetEndPoint(0);
            XYZ scaledPoint = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, point);

            UV scaledUV = ProjectPointToXYPlaneOfLCS(lcs, projDir, scaledPoint);
            uvs.Add(scaledUV);
         }
         return uvs;
      }

      // return null if parent should be completely clipped.
      // TODO: determine whether or not to use face boundary.
      private static IFCAnyHandle ProcessClippingFace(ExporterIFC exporterIFC, CurveLoop outerBoundary, Transform boundaryLCS,
          Transform extrusionBaseLCS, XYZ extrusionDirection, IFCRange range, bool useFaceBoundary, IFCAnyHandle bodyItemHnd)
      {
         if (outerBoundary == null || boundaryLCS == null)
            throw new Exception("Invalid face boundary.");

         double clippingSlant = boundaryLCS.BasisZ.DotProduct(extrusionDirection);
         if (useFaceBoundary)
         {
            if (MathUtil.IsAlmostZero(clippingSlant))
               return bodyItemHnd;
         }

         bool clipCompletely;
         if (!IsInRange(range, boundaryLCS, extrusionDirection, out clipCompletely))
            return clipCompletely ? null : bodyItemHnd;

         if (MathUtil.IsAlmostZero(clippingSlant))
            throw new Exception("Can't create clipping perpendicular to extrusion.");

         IFCFile file = exporterIFC.GetFile();

         XYZ scaledOrig = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, boundaryLCS.Origin);
         XYZ scaledNorm = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, boundaryLCS.BasisZ);
         XYZ scaledXDir = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, boundaryLCS.BasisX);

         IFCAnyHandle planeAxisHnd = ExporterUtil.CreateAxis(file, scaledOrig, scaledNorm, scaledXDir);
         IFCAnyHandle surfHnd = IFCInstanceExporter.CreatePlane(file, planeAxisHnd);

         IFCAnyHandle clippedBodyItemHnd = null;
         IFCAnyHandle halfSpaceHnd = null;

         if (useFaceBoundary)
         {
            IFCAnyHandle boundedCurveHnd;
            if (extrusionBaseLCS != null)
            {
               XYZ projScaledOrigin = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, extrusionBaseLCS.Origin);
               XYZ projScaledX = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, extrusionBaseLCS.BasisX).Normalize();
               XYZ projScaledY = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, extrusionBaseLCS.BasisY).Normalize();
               XYZ projScaledNorm = projScaledX.CrossProduct(projScaledY);

               Transform projScaledLCS = CreateTransformFromVectorsAndOrigin(projScaledX, projScaledY, projScaledNorm, projScaledOrigin);

               IList<UV> polylinePts = TransformAndProjectCurveLoopToPlane(exporterIFC, outerBoundary, projScaledLCS);
               polylinePts.Add(polylinePts[0]);
               boundedCurveHnd = ExporterUtil.CreatePolyline(file, polylinePts);

               IFCAnyHandle boundedAxisHnd = ExporterUtil.CreateAxis(file, projScaledOrigin, projScaledNorm, projScaledX);

               halfSpaceHnd = IFCInstanceExporter.CreatePolygonalBoundedHalfSpace(file, boundedAxisHnd, boundedCurveHnd, surfHnd, false);
            }
            else
            {
               throw new Exception("Can't create non-polygonal face boundary.");
            }
         }
         else
         {
            halfSpaceHnd = IFCInstanceExporter.CreateHalfSpaceSolid(file, surfHnd, false);
         }

         if (halfSpaceHnd == null)
            throw new Exception("Can't create clipping.");

         if (IFCAnyHandleUtil.IsSubTypeOf(bodyItemHnd, IFCEntityType.IfcBooleanClippingResult) ||
             IFCAnyHandleUtil.IsSubTypeOf(bodyItemHnd, IFCEntityType.IfcSweptAreaSolid))
            clippedBodyItemHnd = IFCInstanceExporter.CreateBooleanClippingResult(file, IFCBooleanOperator.Difference,
                bodyItemHnd, halfSpaceHnd);
         else
            clippedBodyItemHnd = IFCInstanceExporter.CreateBooleanResult(file, IFCBooleanOperator.Difference,
                bodyItemHnd, halfSpaceHnd);

         return clippedBodyItemHnd;
      }

      // returns true if either, but not both, the start or end of the extrusion is clipped.
      private static KeyValuePair<bool, bool> CollectionClipsExtrusionEnds(IList<CurveLoop> curveLoopBoundaries, XYZ extrusionDirection,
          IFCRange extrusionRange)
      {
         bool clipStart = false;
         bool clipEnd = false;
         double eps = MathUtil.Eps();

         foreach (CurveLoop curveLoop in curveLoopBoundaries)
         {
            IFCRange loopRange = GetExtrusionRangeOfCurveLoop(curveLoop, extrusionDirection);
            if (loopRange.End >= extrusionRange.End - eps)
               clipEnd = true;
            if (loopRange.Start <= extrusionRange.Start + eps)
               clipStart = true;
         }

         KeyValuePair<bool, bool> clipResults = new KeyValuePair<bool, bool>(clipStart, clipEnd);
         return clipResults;
      }

      /// <summary>
      /// Attempts to create a clipping or recess from a collection of planar faces.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="allowMultipleClipPlanes">Determine whether we are allowed to create more than one clip plane if necessary.</param>
      /// <param name="extrusionBaseLCS">The local coordinate system whose XY plane contains the extrusion base.</param>
      /// <param name="extrusionDirection">The extrusion direction.</param>
      /// <param name="clippingFaces">The original collection of faces.  Currently these should all be planar faces.</param>
      /// <param name="range">The valid range of the extrusion.</param>
      /// <param name="origBodyRepHnd">The original body representation.</param>
      /// <param name="skippedFaces">The faces that weren't handled by this operation.  These may represent openings.</param>
      /// <returns>The new body representation.  If the clipping completely clips the extrusion, this will be null.  Otherwise, this
      /// will be the clipped representation if a clipping was done, or the original representation if not.</returns>
      public static IFCAnyHandle CreateClippingFromPlanarFaces(ExporterIFC exporterIFC, bool allowMultipleClipPlanes, Transform extrusionBaseLCS,
          XYZ extrusionDirection, ICollection<PlanarFace> clippingFaces, IFCRange range, IFCAnyHandle origBodyRepHnd, out ICollection<Face> skippedFaces)
      {
         skippedFaces = null;

         if (IFCAnyHandleUtil.IsNullOrHasNoValue(origBodyRepHnd))
            return null;

         bool polygonalOnly = ExporterCacheManager.ExportOptionsCache.ExportAs2x2;

         IList<CurveLoop> outerCurveLoops = new List<CurveLoop>();
         IList<Transform> outerCurveLoopLCS = new List<Transform>();
         IList<bool> boundaryIsPolygonal = new List<bool>();

         UV faceOriginUV = new UV(0, 0);

         foreach (PlanarFace face in clippingFaces)
         {
            FaceBoundaryType faceBoundaryType;
            CurveLoop curveLoop = GetOuterFaceBoundary(face, null, polygonalOnly, out faceBoundaryType);
            outerCurveLoops.Add(curveLoop);
            boundaryIsPolygonal.Add(faceBoundaryType == FaceBoundaryType.Polygonal);

            PlanarFace planarFace = face as PlanarFace;
            Transform lcs = CreateTransformFromPlanarFace(planarFace);
            outerCurveLoopLCS.Add(lcs);

            if (!curveLoop.IsCounterclockwise(planarFace.FaceNormal))
               curveLoop.Flip();
         }

         int numFaces = clippingFaces.Count;

         KeyValuePair<bool, bool> clipsExtrusionEnds = CollectionClipsExtrusionEnds(outerCurveLoops, extrusionDirection, range);
         if (clipsExtrusionEnds.Key == true || clipsExtrusionEnds.Value == true)
         {
            // Don't clip for a door, window or opening.
            if (!allowMultipleClipPlanes)
               throw new Exception("Unhandled opening.");

            ICollection<int> facesToSkip = new HashSet<int>();
            bool clipStart = (clipsExtrusionEnds.Key == true);
            bool clipBoth = (clipsExtrusionEnds.Key == true && clipsExtrusionEnds.Value == true);
            if (!clipBoth)
            {
               for (int ii = 0; ii < numFaces; ii++)
               {
                  double slant = outerCurveLoopLCS[ii].BasisZ.DotProduct(extrusionDirection);
                  if (!MathUtil.IsAlmostZero(slant))
                  {
                     if (clipStart && (slant > 0.0))
                        throw new Exception("Unhandled clip plane direction.");
                     if (!clipStart && (slant < 0.0))
                        throw new Exception("Unhandled clip plane direction.");
                  }
                  else
                  {
                     facesToSkip.Add(ii);
                  }
               }
            }
            else
            {
               // If we are clipping both the start and end of the extrusion, we have to make sure all of the clipping
               // planes have the same a non-negative dot product relative to one another.
               int clipOrientation = 0;
               for (int ii = 0; ii < numFaces; ii++)
               {
                  double slant = outerCurveLoopLCS[ii].BasisZ.DotProduct(extrusionDirection);
                  if (!MathUtil.IsAlmostZero(slant))
                  {
                     if (slant > 0.0)
                     {
                        if (clipOrientation < 0)
                           throw new Exception("Unhandled clipping orientations.");
                        clipOrientation = 1;
                     }
                     else
                     {
                        if (clipOrientation > 0)
                           throw new Exception("Unhandled clipping orientations.");
                        clipOrientation = -1;
                     }
                  }
                  else
                  {
                     facesToSkip.Add(ii);
                  }
               }
            }

            // Special case: one face is a clip plane.
            if (numFaces == 1)
            {
               return ProcessClippingFace(exporterIFC, outerCurveLoops[0], outerCurveLoopLCS[0], extrusionBaseLCS,
                   extrusionDirection, range, false, origBodyRepHnd);
            }

            IFCAnyHandle newBodyRepHnd = origBodyRepHnd;
            skippedFaces = new HashSet<Face>();

            // originalFaces is an ICollection, so we can't index it.  Instead, we will keep the count ourselves.
            int faceIdx = -1;   // start at -1 so that first increment puts faceIdx at 0.
            foreach (PlanarFace planarFace in clippingFaces)
            {
               faceIdx++;

               if (facesToSkip.Contains(faceIdx))
               {
                  skippedFaces.Add(planarFace);
                  continue;
               }

               newBodyRepHnd = ProcessClippingFace(exporterIFC, outerCurveLoops[faceIdx], outerCurveLoopLCS[faceIdx],
                   extrusionBaseLCS, extrusionDirection, range, true, newBodyRepHnd);
               if (newBodyRepHnd == null)
                  return null;
            }
            return newBodyRepHnd;
         }

         //not handled
         throw new Exception("Unhandled clipping.");
      }

      public static IFCAnyHandle CreateOpeningFromFaces(ExporterIFC exporterIFC, Plane extrusionBasePlane,
          XYZ extrusionDirection, ICollection<Face> faces, IFCRange range, IFCAnyHandle origBodyRepHnd)
      {
         // If we have no faces, return the original value.
         if (faces.Count == 0)
            return origBodyRepHnd;

         // We will attempt to "sew" the faces, and see what we have left over.  Depending on what we have, we have an opening, recess, or clipping.

         // top and bottom profile curves to create extrusion
         IDictionary<Face, IList<Curve>> boundaryCurves = new Dictionary<Face, IList<Curve>>();
         // curves on same side face to check if they are valid for extrusion
         IDictionary<Face, IList<Curve>> boundaryCurvesInSameExistingFace = new Dictionary<Face, IList<Curve>>();

         foreach (Face face in faces)
         {
            EdgeArrayArray faceBoundaries = face.EdgeLoops;
            // We only know how to deal with the outer loop; we'll throw if we have multiple boundaries.
            if (faceBoundaries.Size != 1)
               throw new Exception("Can't process faces with inner boundaries.");

            EdgeArray faceBoundary = faceBoundaries.get_Item(0);
            foreach (Edge edge in faceBoundary)
            {
               Face face1 = edge.GetFace(0);
               Face face2 = edge.GetFace(1);
               Face missingFace = null;
               Face existingFace = null;
               if (!faces.Contains(face1))
               {
                  missingFace = face1;
                  existingFace = face2;
               }
               else if (!faces.Contains(face2))
               {
                  missingFace = face2;
                  existingFace = face1;
               }

               if (missingFace != null)
               {
                  Curve curve = edge.AsCurve();
                  if (!boundaryCurves.ContainsKey(missingFace))
                     boundaryCurves[missingFace] = new List<Curve>();
                  boundaryCurves[missingFace].Add(curve);
                  if (!boundaryCurvesInSameExistingFace.ContainsKey(existingFace))
                     boundaryCurvesInSameExistingFace[existingFace] = new List<Curve>();
                  boundaryCurvesInSameExistingFace[existingFace].Add(curve);
               }
            }
         }

         //might be recess
         if (boundaryCurves.Count == 1)
         {
            // boundaryCurves contains one curve loop of top profile of an extrusion
            // try to find bottom profile
            IList<Curve> curves1 = boundaryCurves.Values.ElementAt(0);
            CurveLoop curveloop = CurveLoop.Create(curves1);

            // find the parallel face
            XYZ normal = curveloop.GetPlane().Normal;

            PlanarFace recessFace = null;
            foreach (Face face in faces)
            {
               PlanarFace planarFace = face as PlanarFace;
               if (planarFace != null && MathUtil.VectorsAreParallel(planarFace.FaceNormal, normal))
               {
                  if (recessFace == null)
                     recessFace = planarFace;
                  else
                     throw new Exception("Can't handle.");
               }
            }

            if (recessFace != null)
            {
               EdgeArrayArray edgeLoops = recessFace.EdgeLoops;
               if (edgeLoops.Size != 1)
                  throw new Exception("Can't handle.");

               EdgeArray edges = edgeLoops.get_Item(0);

               IList<Edge> recessFaceEdges = new List<Edge>();
               foreach (Edge edge in edges)
               {
                  Face sideFace = edge.GetFace(0);
                  if (sideFace == recessFace)
                     sideFace = edge.GetFace(1);

                  // there should be already one exist during above processing
                  if (!boundaryCurvesInSameExistingFace.ContainsKey(sideFace))
                     throw new Exception("Can't handle.");
                  boundaryCurvesInSameExistingFace[sideFace].Add(edge.AsCurve());
               }
            }
         }
         else if (boundaryCurves.Count == 2)
         {
            // might be an internal opening, process them later
            // do nothing now
         }
         else if (boundaryCurves.Count == 3) //might be an opening on an edge
         {
            IList<Curve> curves1 = boundaryCurves.Values.ElementAt(0);
            IList<Curve> curves2 = boundaryCurves.Values.ElementAt(1);
            IList<Curve> curves3 = boundaryCurves.Values.ElementAt(2);

            IList<Curve> firstValidCurves = null;
            IList<Curve> secondValidCurves = null;

            PlanarFace face1 = boundaryCurves.Keys.ElementAt(0) as PlanarFace;
            PlanarFace face2 = boundaryCurves.Keys.ElementAt(1) as PlanarFace;
            PlanarFace face3 = boundaryCurves.Keys.ElementAt(2) as PlanarFace;

            if (face1 == null || face2 == null || face3 == null)
            {
               //Error
               throw new Exception("Can't handle.");
            }

            Face removedFace = null;

            // find two parallel faces
            if (MathUtil.VectorsAreParallel(face1.FaceNormal, face2.FaceNormal))
            {
               firstValidCurves = curves1;
               secondValidCurves = curves2;
               removedFace = face3;
            }
            else if (MathUtil.VectorsAreParallel(face1.FaceNormal, face3.FaceNormal))
            {
               firstValidCurves = curves1;
               secondValidCurves = curves3;
               removedFace = face2;
            }
            else if (MathUtil.VectorsAreParallel(face2.FaceNormal, face3.FaceNormal))
            {
               firstValidCurves = curves2;
               secondValidCurves = curves3;
               removedFace = face1;
            }

            // remove the third one and its edge curves
            if (removedFace != null)
            {
               foreach (Curve curve in boundaryCurves[removedFace])
               {
                  foreach (KeyValuePair<Face, IList<Curve>> faceEdgePair in boundaryCurvesInSameExistingFace)
                  {
                     if (faceEdgePair.Value.Contains(curve))
                        boundaryCurvesInSameExistingFace[faceEdgePair.Key].Remove(curve);
                  }
               }
               boundaryCurves.Remove(removedFace);
            }

            // sew, “closing” them with a simple line
            IList<IList<Curve>> curvesCollection = new List<IList<Curve>>();
            if (firstValidCurves != null)
               curvesCollection.Add(firstValidCurves);
            if (secondValidCurves != null)
               curvesCollection.Add(secondValidCurves);

            foreach (IList<Curve> curves in curvesCollection)
            {
               if (curves.Count < 2) //not valid
                  throw new Exception("Can't handle.");

               XYZ end0 = curves[0].GetEndPoint(0);
               XYZ end1 = curves[0].GetEndPoint(1);

               IList<Curve> processedCurves = new List<Curve>();
               processedCurves.Add(curves[0]);
               curves.Remove(curves[0]);

               Curve nextCurve = null;
               Curve preCurve = null;

               // find the end points on the edges not connected
               while (curves.Count > 0)
               {
                  foreach (Curve curve in curves)
                  {
                     XYZ curveEnd0 = curve.GetEndPoint(0);
                     XYZ curveEnd1 = curve.GetEndPoint(1);
                     if (end1.IsAlmostEqualTo(curveEnd0))
                     {
                        nextCurve = curve;
                        end1 = curveEnd1;
                        break;
                     }
                     else if (end0.IsAlmostEqualTo(curveEnd1))
                     {
                        preCurve = curve;
                        end0 = curveEnd0;
                        break;
                     }
                  }

                  if (nextCurve != null)
                  {
                     processedCurves.Add(nextCurve);
                     curves.Remove(nextCurve);
                     nextCurve = null;
                  }
                  else if (preCurve != null)
                  {
                     processedCurves.Insert(0, preCurve);
                     curves.Remove(preCurve);
                     preCurve = null;
                  }
                  else
                     throw new Exception("Can't process edges.");
               }

               // connect them with a simple line
               Curve newCurve = Line.CreateBound(end1, end0);
               processedCurves.Add(newCurve);
               if (!boundaryCurvesInSameExistingFace.ContainsKey(removedFace))
                  boundaryCurvesInSameExistingFace[removedFace] = new List<Curve>();
               boundaryCurvesInSameExistingFace[removedFace].Add(newCurve);
               foreach (Curve curve in processedCurves)
               {
                  curves.Add(curve);
               }
            }
         }
         else
            throw new Exception("Can't handle.");

         // now we should have 2 boundary curve loops
         IList<Curve> firstCurves = boundaryCurves.Values.ElementAt(0);
         IList<Curve> secondCurves = boundaryCurves.Values.ElementAt(1);
         PlanarFace firstFace = boundaryCurves.Keys.ElementAt(0) as PlanarFace;
         PlanarFace secondFace = boundaryCurves.Keys.ElementAt(1) as PlanarFace;

         if (firstFace == null || secondFace == null)
         {
            //Error, can't handle this
            throw new Exception("Can't handle.");
         }

         if (firstCurves.Count != secondCurves.Count)
         {
            //Error, can't handle this
            throw new Exception("Can't handle.");
         }

         CurveLoop curveLoop1 = null;
         CurveLoop curveLoop2 = null;

         SortCurves(firstCurves);
         curveLoop1 = CurveLoop.Create(firstCurves);
         SortCurves(secondCurves);
         curveLoop2 = CurveLoop.Create(secondCurves);

         if (curveLoop1.IsOpen() || curveLoop2.IsOpen() || !curveLoop1.HasPlane() || !curveLoop2.HasPlane())
         {
            //Error, can't handle this
            throw new Exception("Can't handle.");
         }

         Plane plane1 = curveLoop1.GetPlane();
         Plane plane2 = curveLoop2.GetPlane();

         if (!curveLoop1.IsCounterclockwise(plane1.Normal))
         {
            curveLoop1.Flip();
         }

         if (!curveLoop2.IsCounterclockwise(plane2.Normal))
         {
            curveLoop2.Flip();
         }

         // Check that the faces are planar and parallel to each other.
         // If the faces have normals that are parallel to the extrusion direction, it could be better to either:
         // 1. export the opening as an IfcOpeningElement, or
         // 2. add the boundary as an inner boundary of the extrusion.
         // as these would be less sensitive to Boolean operation errors.
         // This will be considered for a future improvement.

         if (!MathUtil.VectorsAreParallel(plane1.Normal, plane2.Normal))
         {
            //Error, can't handle this
            throw new Exception("Can't handle.");
         }

         //get the distance
         XYZ origDistance = plane1.Origin - plane2.Origin;
         double planesDistance = Math.Abs(origDistance.DotProduct(plane1.Normal));

         // check the curves on top and bottom profiles are “identical”
         foreach (KeyValuePair<Face, IList<Curve>> faceEdgeCurvePair in boundaryCurvesInSameExistingFace)
         {
            IList<Curve> curves = faceEdgeCurvePair.Value;
            if (curves.Count != 2)
            {
               //Error, can't handle this
               throw new Exception("Can't handle.");
            }

            Curve edgeCurve1 = curves[0];
            Curve edgeCurve2 = curves[1];
            Face sideFace = faceEdgeCurvePair.Key;

            if (!MathUtil.IsAlmostEqual(edgeCurve1.GetEndPoint(0).DistanceTo(edgeCurve2.GetEndPoint(1)), planesDistance)
              || !MathUtil.IsAlmostEqual(edgeCurve1.GetEndPoint(1).DistanceTo(edgeCurve2.GetEndPoint(0)), planesDistance))
            {
               //Error, can't handle this
               throw new Exception("Can't handle.");
            }

            if (edgeCurve1 is Line)
            {
               if (!(edgeCurve2 is Line) || !(sideFace is PlanarFace))
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }
            }
            else if (edgeCurve1 is Arc)
            {
               if (!(edgeCurve2 is Arc) || !(sideFace is CylindricalFace))
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               Arc arc1 = edgeCurve1 as Arc;
               Arc arc2 = edgeCurve2 as Arc;

               if (!MathUtil.IsAlmostEqual(arc1.Center.DistanceTo(arc2.Center), planesDistance))
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               XYZ sideFaceAxis = (sideFace as CylindricalFace).Axis;
               if (!MathUtil.VectorsAreOrthogonal(sideFaceAxis, extrusionDirection))
               {
                  throw new Exception("Can't handle.");
               }
            }
            else if (edgeCurve1 is Ellipse)
            {
               if (!(edgeCurve2 is Ellipse) || !(sideFace is RuledFace) || !(sideFace as RuledFace).RulingsAreParallel)
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               Ellipse ellipse1 = edgeCurve1 as Ellipse;
               Ellipse ellipse2 = edgeCurve2 as Ellipse;

               if (!MathUtil.IsAlmostEqual(ellipse1.Center.DistanceTo(ellipse2.Center), planesDistance)
                   || !MathUtil.IsAlmostEqual(ellipse1.RadiusX, ellipse2.RadiusX) || !MathUtil.IsAlmostEqual(ellipse1.RadiusY, ellipse2.RadiusY))
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }
            }
            else if (edgeCurve1 is HermiteSpline)
            {
               if (!(edgeCurve2 is HermiteSpline) || !(sideFace is RuledFace) || !(sideFace as RuledFace).RulingsAreParallel)
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               HermiteSpline hermiteSpline1 = edgeCurve1 as HermiteSpline;
               HermiteSpline hermiteSpline2 = edgeCurve2 as HermiteSpline;

               IList<XYZ> controlPoints1 = hermiteSpline1.ControlPoints;
               IList<XYZ> controlPoints2 = hermiteSpline2.ControlPoints;

               int controlPointCount = controlPoints1.Count;
               if (controlPointCount != controlPoints2.Count)
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               for (int ii = 0; ii < controlPointCount; ii++)
               {
                  if (!MathUtil.IsAlmostEqual(controlPoints1[ii].DistanceTo(controlPoints2[controlPointCount - ii - 1]), planesDistance))
                  {
                     //Error, can't handle this
                     throw new Exception("Can't handle.");
                  }
               }

               DoubleArray parameters1 = hermiteSpline1.Parameters;
               DoubleArray parameters2 = hermiteSpline1.Parameters;

               int parametersCount = parameters1.Size;
               if (parametersCount != parameters2.Size)
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               for (int ii = 0; ii < parametersCount; ii++)
               {
                  if (!MathUtil.IsAlmostEqual(parameters1.get_Item(ii), parameters2.get_Item(ii)))
                  {
                     //Error, can't handle this
                     throw new Exception("Can't handle.");
                  }
               }
            }
            else if (edgeCurve1 is NurbSpline)
            {
               if (!(edgeCurve2 is NurbSpline) || !(sideFace is RuledFace) || !(sideFace as RuledFace).RulingsAreParallel)
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               NurbSpline nurbSpline1 = edgeCurve1 as NurbSpline;
               NurbSpline nurbSpline2 = edgeCurve2 as NurbSpline;

               IList<XYZ> controlPoints1 = nurbSpline1.CtrlPoints;
               IList<XYZ> controlPoints2 = nurbSpline2.CtrlPoints;

               int controlPointCount = controlPoints1.Count;
               if (controlPointCount != controlPoints2.Count)
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               for (int i = 0; i < controlPointCount; i++)
               {
                  if (!MathUtil.IsAlmostEqual(controlPoints1[i].DistanceTo(controlPoints2[controlPointCount - i - 1]), planesDistance))
                  {
                     //Error, can't handle this
                     throw new Exception("Can't handle.");
                  }
               }

               DoubleArray weights1 = nurbSpline1.Weights;
               DoubleArray weights2 = nurbSpline2.Weights;

               int weightsCount = weights1.Size;
               if (weightsCount != weights2.Size)
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               for (int ii = 0; ii < weightsCount; ii++)
               {
                  if (!MathUtil.IsAlmostEqual(weights1.get_Item(ii), weights2.get_Item(ii)))
                  {
                     //Error, can't handle this
                     throw new Exception("Can't handle.");
                  }
               }

               DoubleArray knots1 = nurbSpline1.Knots;
               DoubleArray knots2 = nurbSpline2.Knots;

               int knotsCount = knots1.Size;
               if (knotsCount != knots2.Size)
               {
                  //Error, can't handle this
                  throw new Exception("Can't handle.");
               }

               for (int i = 0; i < knotsCount; i++)
               {
                  if (!MathUtil.IsAlmostEqual(knots1.get_Item(i), knots2.get_Item(i)))
                  {
                     //Error, can't handle this
                     throw new Exception("Can't handle.");
                  }
               }
            }
            else
            {
               //Error, can't handle this
               throw new Exception("Can't handle.");
            }
         }

         XYZ extDir = plane2.Origin - plane1.Origin;
         XYZ plane1Normal = plane1.Normal;
         int vecParallel = MathUtil.VectorsAreParallel2(extDir, plane1Normal);
         if (vecParallel == 1)
         {
            extDir = plane1Normal;
         }
         else if (vecParallel == -1)
         {
            extDir = -plane1Normal;
         }
         else
            throw new Exception("Can't handle.");

         IList<CurveLoop> origCurveLoops = new List<CurveLoop>();
         origCurveLoops.Add(curveLoop1);

         double scaledPlanesDistance = UnitUtil.ScaleLength(planesDistance);
         Transform plane1LCS = GeometryUtil.CreateTransformFromPlane(plane1);
         IFCAnyHandle extrusionHandle = ExtrusionExporter.CreateExtrudedSolidFromCurveLoop(exporterIFC, null, origCurveLoops, plane1LCS, extDir, scaledPlanesDistance, false, out _);

         IFCAnyHandle booleanBodyItemHnd = IFCInstanceExporter.CreateBooleanResult(exporterIFC.GetFile(), IFCBooleanOperator.Difference,
             origBodyRepHnd, extrusionHandle);

         return booleanBodyItemHnd;
      }

      /// <summary>
      /// Common method to create a poly line segment for an IfcCompositeCurve.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="points">The line points.</param>
      /// <returns>The handle.</returns>
      static IFCAnyHandle CreatePolyLineSegmentCommon(ExporterIFC exporterIFC, IList<XYZ> points)
      {
         if (exporterIFC == null || points == null)
            throw new ArgumentNullException();

         int count = points.Count;
         if (count < 2)
            throw new InvalidOperationException("Invalid polyline.");

         bool isClosed = points[0].IsAlmostEqualTo(points[count - 1]);
         if (isClosed)
            count--;

         if (count < 2)
            throw new InvalidOperationException("Invalid polyline.");

         IFCFile file = exporterIFC.GetFile();
         List<IFCAnyHandle> polyLinePoints = new List<IFCAnyHandle>();
         for (int ii = 0; ii < count; ii++)
         {
            XYZ point = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, points[ii]);
            IFCAnyHandle pointHandle = ExporterUtil.CreateCartesianPoint(file, point);
            polyLinePoints.Add(pointHandle);
         }

         if (isClosed)
            polyLinePoints.Add(polyLinePoints[0]);

         return IFCInstanceExporter.CreatePolyline(file, polyLinePoints);
      }

      /// <summary>
      /// Creates an IFC line segment for an IfcCompositeCurve from a Revit line object.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="line">The line.</param>
      /// <returns>The line handle.</returns>
      public static IFCAnyHandle CreateLineSegment(ExporterIFC exporterIFC, Line line)
      {
         List<XYZ> points = new()
         {
            line.GetEndPoint(0),
            line.GetEndPoint(1)
         };
         return CreatePolyLineSegmentCommon(exporterIFC, points);
      }

      /// <summary>
      /// Returns an Ifc handle corresponding to the curve segment after processing its bounds, if any.
      /// </summary>
      /// <param name="file">The IFCFile handle.</param>
      /// <param name="curveHnd">The unbounded curve handle.</param>
      /// <param name="curve">The Revit curve to process.</param>
      /// <returns>The original handle if the curve is unbound, or a new IfcTrimmedCurve handle if bounded.</returns>
      /// <remarks>This routine expects that bounded curves are periodic curves with a periodicity of 2*PI.</remarks>
      private static IFCAnyHandle CreateBoundsIfNecessary(IFCFile file, IFCAnyHandle curveHnd, Curve curve)
      {
         if (!curve.IsBound)
            return curveHnd;

         if (!curve.IsCyclic || !MathUtil.IsAlmostEqual(curve.Period, 2 * Math.PI))
            throw new InvalidOperationException("Expected periodic curve with period of 2*PI.");

         double endParam0 = curve.GetEndParameter(0);
         double endParam1 = curve.GetEndParameter(1);

         IFCData firstParam = IFCDataUtil.CreateAsParameterValue(UnitUtil.ScaleAngle(MathUtil.PutInRange(endParam0, Math.PI, 2 * Math.PI)));
         IFCData secondParam = IFCDataUtil.CreateAsParameterValue(UnitUtil.ScaleAngle(MathUtil.PutInRange(endParam1, Math.PI, 2 * Math.PI)));

         // todo: check that firstParam != secondParam.
         HashSet<IFCData> trim1 = new HashSet<IFCData>();
         trim1.Add(firstParam);
         HashSet<IFCData> trim2 = new HashSet<IFCData>();
         trim2.Add(secondParam);

         return IFCInstanceExporter.CreateTrimmedCurve(file, curveHnd, trim1, trim2, true, IFCTrimmingPreference.Parameter);
      }

      /// <summary>
      /// Creates an IFC arc segment for an IfcCompositeCurve from a Revit arc object.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="arc">The arc.</param>
      /// <returns>The arc handle.</returns>
      public static IFCAnyHandle CreateArcSegment(ExporterIFC exporterIFC, Arc arc)
      {
         double arcRadius = UnitUtil.ScaleLength(arc.Radius);
         if (!IFCInstanceExporter.ValidateCircle(arcRadius))
         {
            return null;
         }

         IFCFile file = exporterIFC.GetFile();

         XYZ centerPoint = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, arc.Center);

         IFCAnyHandle centerPointHandle = ExporterUtil.CreateCartesianPoint(file, centerPoint);

         XYZ xDirection = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, arc.XDirection);
         IFCAnyHandle axis = ExporterUtil.CreateAxis2Placement3D(file, centerPoint, arc.Normal, xDirection);

         IFCAnyHandle circle = IFCInstanceExporter.CreateCircle(file, axis, arcRadius);
         return CreateBoundsIfNecessary(file, circle, arc);
      }

      /// <summary>
      /// Creates an IFC ellipse segment for an IfcCompositeCurve from a Revit ellipse object.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="ellipticalArc">The elliptical arc.</param>
      /// <returns>The ellipse handle.</returns>
      public static IFCAnyHandle CreateEllipticalArcSegment(ExporterIFC exporterIFC, Ellipse ellipticalArc)
      {
         double ellipseRadiusX = UnitUtil.ScaleLength(ellipticalArc.RadiusX);
         double ellipseRadiusY = UnitUtil.ScaleLength(ellipticalArc.RadiusY);
         if (!IFCInstanceExporter.ValidateEllipse(ellipseRadiusX, ellipseRadiusY))
         {
            return null;
         }

         IFCFile file = exporterIFC.GetFile();

         XYZ centerPoint = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, ellipticalArc.Center);

         IFCAnyHandle centerPointHandle = ExporterUtil.CreateCartesianPoint(file, centerPoint);

         XYZ xDirection = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, ellipticalArc.XDirection);
         IFCAnyHandle axis = ExporterUtil.CreateAxis2Placement3D(file, centerPoint, ellipticalArc.Normal, xDirection);

         IFCAnyHandle ellipse = IFCInstanceExporter.CreateEllipse(file, axis, ellipseRadiusX, ellipseRadiusY);
         return CreateBoundsIfNecessary(file, ellipse, ellipticalArc);
      }

      /// <summary>
      /// Creates an IFC composite curve from an array of curves.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="curves">The curves.</param>
      /// <returns>The IfcCompositeCurve handle.</returns>
      /// <remarks>This function tessellates all curve types except lines, arcs, and ellipses.</remarks>
      private static IFCAnyHandle CreateCompositeCurve(ExporterIFC exporterIFC, IList<Curve> curves)
      {
         IFCFile file = exporterIFC.GetFile();
         List<IFCAnyHandle> segments = new List<IFCAnyHandle>();
         foreach (Curve curve in curves)
         {
            if (curve == null)
               continue;

            IFCAnyHandle curveHandle = null;
            if (curve is Line)
            {
               curveHandle = CreateLineSegment(exporterIFC, curve as Line);
            }
            else if (curve is Arc)
            {
               curveHandle = CreateArcSegment(exporterIFC, curve as Arc);
            }
            else if (curve is Ellipse)
            {
               curveHandle = CreateEllipticalArcSegment(exporterIFC, curve as Ellipse);
            }
            else
            {
               IList<XYZ> points = curve.Tessellate();
               curveHandle = CreatePolyLineSegmentCommon(exporterIFC, points);
            }

            if (!IFCAnyHandleUtil.IsNullOrHasNoValue(curveHandle))
            {
               segments.Add(IFCInstanceExporter.CreateCompositeCurveSegment(file, IFCTransitionCode.Continuous, true, curveHandle));
            }
         }

         if (segments.Count > 0)
         {
            return IFCInstanceExporter.CreateCompositeCurve(file, segments, IFCLogical.False);
         }

         return null;
      }

      /// <summary>
      /// Creates an IFC composite or indexed curve from an array of curves.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="curves">The list of curves.</param>
      /// <param name="lcs">The local coordinate system whose XY plane the curves are projected on.</param>
      /// <param name="projDir">The project direction.</param>
      /// <returns>The created curve.</returns>
      public static IFCAnyHandle CreateCompositeOrIndexedCurve(ExporterIFC exporterIFC, IList<Curve> curves, Transform lcs, XYZ projDir)
      {
         IFCAnyHandle compositeCurve;

         if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
         {
            compositeCurve = CreatePolyCurveFromCurveLoop(exporterIFC, curves, lcs, projDir);
         }
         else
         {
            compositeCurve = CreateCompositeCurve(exporterIFC, curves);
         }

         return compositeCurve;
      }

      /// <summary>
      /// Create an IfcSweptDiskSolid from a base curve.
      /// </summary>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      /// <param name="file">The IFCFile.</param>
      /// <param name="centerCurve">The directrix of the sweep.</param>
      /// <param name="radius">The outer radius.</param>
      /// <param name="innerRadius">The optional inner radius.</param>
      /// <returns>The IfcSweptDiskSolid.</returns>
      public static IFCAnyHandle CreateSweptDiskSolid(ExporterIFC exporterIFC, IFCFile file, Curve centerCurve, double radius, double? innerRadius)
      {
         if (centerCurve == null || radius < MathUtil.Eps() || (innerRadius.HasValue && innerRadius.Value > radius - MathUtil.Eps()))
            return null;

         IList<Curve> curves = new List<Curve>();
         double endParam = 0.0;
         if (centerCurve is Arc || centerCurve is Ellipse)
         {
            if (centerCurve.IsBound)
               endParam = UnitUtil.ScaleAngle(centerCurve.GetEndParameter(1) - centerCurve.GetEndParameter(0));
            else
               endParam = UnitUtil.ScaleAngle(2 * Math.PI);
         }
         else
            endParam = 1.0;
         curves.Add(centerCurve);

         IFCAnyHandle compositeCurve = GeometryUtil.CreateCompositeOrIndexedCurve(exporterIFC, curves, null, null);
         return IFCInstanceExporter.CreateSweptDiskSolid(file, compositeCurve, radius, innerRadius, 0, endParam);
      }


      /// <summary>
      /// Sorts curves to allow CurveLoop creation that means each curve end must meet next curve start.
      /// </summary>
      /// <param name="curves">The curves.</param>
      public static void SortCurves(IList<Curve> curves)
      {
         IList<Curve> sortedCurves = new List<Curve>();
         Curve currentCurve = curves[0];
         sortedCurves.Add(currentCurve);

         bool found = false;

         do
         {
            found = false;
            for (int i = 1; i < curves.Count; i++)
            {
               Curve curve = curves[i];
               if (currentCurve.GetEndPoint(1).IsAlmostEqualTo(curve.GetEndPoint(0)))
               {
                  sortedCurves.Add(curve);
                  currentCurve = curve;
                  found = true;
                  break;
               }
            }
         } while (found);

         if (sortedCurves.Count != curves.Count)
            throw new InvalidOperationException("Failed to sort curves.");

         // add back
         curves.Clear();
         foreach (Curve curve in sortedCurves)
         {
            curves.Add(curve);
         }
      }

      /// <summary>
      /// Get the color RGB values from color integer value.
      /// </summary>
      /// <param name="color">The color integer value</param>
      /// <param name="blueValue">The blue value.</param>
      /// <param name="greenValue">The green value.</param>
      /// <param name="redValue">The red value.</param>
      public static void GetRGBFromIntValue(int color, out double blueValue, out double greenValue, out double redValue)
      {
         blueValue = ((double)((color & 0xff0000) >> 16)) / 255.0;
         greenValue = ((double)((color & 0xff00) >> 8)) / 255.0;
         redValue = ((double)(color & 0xff)) / 255.0;
      }

      /// <summary>
      /// Gets bounding box of geometries.
      /// </summary>
      /// <param name="geometryList">The geometries.</param>
      /// <returns>The bounding box.</returns>
      public static BoundingBoxXYZ GetBBoxOfGeometries(IList<GeometryObject> geometryList)
      {
         BoundingBoxXYZ bbox = new BoundingBoxXYZ();
         bool bboxIsSet = false;
         bbox.Min = new XYZ(1, 1, 1);
         bbox.Max = new XYZ(0, 0, 0);

         foreach (GeometryObject geomObject in geometryList)
         {
            BoundingBoxXYZ localBbox = null;
            if (geomObject is GeometryElement)
            {
               localBbox = (geomObject as GeometryElement).GetBoundingBox();
            }
            else if (geomObject is Solid)
            {
               localBbox = (geomObject as Solid).GetBoundingBox();
            }

            if (localBbox != null)
            {
               Transform trf = localBbox.Transform;

               XYZ origMin = bbox.Min;
               XYZ origMax = bbox.Max;
               XYZ localMin = trf.OfPoint(localBbox.Min);
               XYZ localMax = trf.OfPoint(localBbox.Max);
               if (bboxIsSet)
               {
                  double newCornerX, newCornerY, newCornerZ;
                  newCornerX = Math.Min(origMin.X, localMin.X);
                  newCornerY = Math.Min(origMin.Y, localMin.Y);
                  newCornerZ = Math.Min(origMin.Z, localMin.Z);
                  bbox.Min = new XYZ(newCornerX, newCornerY, newCornerZ);
                  newCornerX = Math.Max(origMax.X, localMax.X);
                  newCornerY = Math.Max(origMax.Y, localMax.Y);
                  newCornerZ = Math.Max(origMax.Z, localMax.Z);
                  bbox.Max = new XYZ(newCornerX, newCornerY, newCornerZ);
               }
               else
               {
                  bboxIsSet = true;
                  bbox.Min = localMin;
                  bbox.Max = localMax;
               }
            }
         }

         if (!bboxIsSet)
            return null;

         if (bbox.Min.X > bbox.Max.X || bbox.Min.Y > bbox.Max.Y || bbox.Min.Z > bbox.Max.Z)
            return null;

         return bbox;
      }

      /// <summary>
      /// Gets a scaled transform.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <returns>The transform.</returns>
      public static Transform GetScaledTransform(ExporterIFC exporterIFC)
      {
         XYZ scaledOrigin = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, XYZ.Zero);
         XYZ scaledXDir = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, XYZ.BasisX);
         XYZ scaledYDir = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, XYZ.BasisY);
         XYZ scaledZDir = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, XYZ.BasisZ);

         Transform scaledTrf = Transform.Identity;
         scaledTrf.Origin = scaledOrigin;
         scaledTrf.BasisX = scaledXDir;
         scaledTrf.BasisY = scaledYDir;
         scaledTrf.BasisZ = scaledZDir;

         return scaledTrf;
      }

      /// <summary>
      /// Gets ratios of a direction.
      /// </summary>
      /// <param name="dirHandle">The direction handle.</param>
      /// <returns>The XYZ represents the ratios.</returns>
      public static XYZ GetDirectionRatios(IFCAnyHandle dirHandle)
      {
         if (!IFCAnyHandleUtil.IsNullOrHasNoValue(dirHandle))
         {
            List<double> ratios = IFCAnyHandleUtil.GetAggregateDoubleAttribute<List<double>>(dirHandle, "DirectionRatios");
            int size = ratios.Count;
            double x = size > 0 ? ratios[0] : 0;
            double y = size > 1 ? ratios[1] : 0;
            double z = size > 2 ? ratios[2] : 0;
            return new XYZ(x, y, z);
         }
         return null;
      }

      /// <summary>
      /// Gets coordinates of a point.
      /// </summary>
      /// <param name="cartesianPoint">The point handle.</param>
      /// <returns>The XYZ represents coordinates.</returns>
      public static XYZ GetCoordinates(IFCAnyHandle cartesianPoint)
      {
         if (!IFCAnyHandleUtil.IsNullOrHasNoValue(cartesianPoint))
         {
            List<double> ratios = IFCAnyHandleUtil.GetAggregateDoubleAttribute<List<double>>(cartesianPoint, "Coordinates");
            int size = ratios.Count;
            double x = size > 0 ? ratios[0] : 0;
            double y = size > 1 ? ratios[1] : 0;
            double z = size > 2 ? ratios[2] : 0;
            return new XYZ(x, y, z);
         }
         return null;
      }

      /// <summary>
      /// Checks if a CurveLoop is inside another CurveLoop.
      /// </summary>
      /// <param name="innerLoop">The inner loop.</param>
      /// <param name="outerLoop">The outer loop.</param>
      /// <returns>True if the CurveLoop is inside the other CurveLoop.</returns>
      public static bool CurveLoopsInside(CurveLoop innerLoop, CurveLoop outerLoop)
      {
         if (innerLoop == null || outerLoop == null)
            return false;

         if (!innerLoop.HasPlane() || !outerLoop.HasPlane() || outerLoop.IsOpen())
            return false;

         XYZ outerOrigin = outerLoop.GetPlane().Origin;
         XYZ outerNormal = outerLoop.GetPlane().Normal;

         foreach (Curve innerCurve in innerLoop)
         {
            XYZ innerCurveEnd0 = innerCurve.GetEndPoint(0);

            XYZ outerOriginToEnd0 = innerCurveEnd0 - outerOrigin;
            if (!MathUtil.VectorsAreOrthogonal(outerOriginToEnd0, outerNormal))
               return false;

            Line line0 = Line.CreateBound(innerCurveEnd0, outerOrigin);
            foreach (Curve outerCurve in outerLoop)
            {
               if (SetComparisonResult.Overlap == line0.Intersect(outerCurve, CurveIntersectResultOption.Simple)?.Result)
               {
                  return false;
               }
            }
         }

         return true;
      }

      /// <summary>
      /// Checks if a CurveLoop intersects with another CurveLoop.
      /// </summary>
      /// <param name="loop1">The CurveLoop.</param>
      /// <param name="loop2">The CurveLoop.</param>
      /// <returns>True if the CurveLoop intersects with the other CurveLoop.</returns>
      public static bool CurveLoopsIntersect(CurveLoop loop1, CurveLoop loop2)
      {
         if (loop1 == null || loop2 == null)
            return false;

         foreach (Curve curve1 in loop1)
         {
            foreach (Curve curve2 in loop2)
            {
               if (SetComparisonResult.Overlap != curve1.Intersect(curve2, CurveIntersectResultOption.Simple)?.Result)
               {
                  return true;
               }
            }
         }
         return false;
      }

      /// <summary>
      /// Computes height and width of a curve loop with respect to a projection plane.
      /// </summary>
      /// <param name="curveLoop">The curve loop.</param>
      /// <param name="lcs">The local coordinate system whose XY plane the represents the projection plane.</param>
      /// <param name="height">The height.</param>
      /// <param name="width">The width.</param>
      /// <returns>True if success, false if fail.</returns>
      public static bool ComputeHeightWidthOfCurveLoop(CurveLoop curveLoop, Transform lcs, out double height, out double width)
      {
         height = 0.0;
         width = 0.0;

         Plane localPlane = null;
         try
         {
            localPlane = CreatePlaneFromTransformNearOrigin(lcs);
         }
         catch
         {
            return false;
         }

         if (localPlane == null)
         {
            try
            {
               localPlane = curveLoop.GetPlane();
            }
            catch
            {
               return false;
            }
         }

         if (curveLoop.IsRectangular(localPlane))
         {
            height = curveLoop.GetRectangularHeight(localPlane);
            width = curveLoop.GetRectangularWidth(localPlane);
            return true;
         }
         else
            return false;
      }

      /// <summary>
      /// Computes the height and width of a CurveLoop.
      /// </summary>
      /// <param name="curveLoop">The CurveLoop.</param>
      /// <param name="height">The height.</param>
      /// <param name="width">The width.</param>
      /// <returns>True if gets the values successfully.</returns>
      public static bool ComputeHeightWidthOfCurveLoop(CurveLoop curveLoop, XYZ expectedWidthDir, out double height, out double width)
      {
         bool result = false;
         height = width = 0;

         if (!curveLoop.HasPlane())
            return result;

         Plane plane = curveLoop.GetPlane();
         Transform lcs = CreateTransformFromPlane(plane);

         result = ComputeHeightWidthOfCurveLoop(curveLoop, lcs, out height, out width);

         // The plane might be flipped. Swap height and width in this case
         if (expectedWidthDir != null && MathUtil.VectorsAreParallel(expectedWidthDir, plane.YVec))
            (height, width) = (width, height);

         return result;
      }

      /// <summary>
      /// Computes the area defined by a polygonal loop.
      /// </summary>
      /// <param name="loop">The polygonal loop.</param>
      /// <param name="normal">The normal of the face.</param>
      /// <param name="refPoint">Reference point for area computation.</param>
      /// <returns>The area.</returns>
      public static double ComputePolygonalLoopArea(IList<XYZ> loop, XYZ normal, XYZ refPoint)
      {
         double area = 0.0;
         int numVertices = loop.Count;
         for (int ii = 0; ii < numVertices; ii++)
         {
            XYZ currEdge = loop[(ii + 1) % numVertices] - loop[ii];
            double length = currEdge.GetLength();

            XYZ heightVec = normal.CrossProduct(currEdge).Normalize();
            XYZ otherEdge = refPoint - loop[ii];
            double height = heightVec.DotProduct(otherEdge);
            area += (length * height);
         }
         return area / 2.0;
      }

      /// <summary>
      /// Gets the volume of a solid, if it is possible.
      /// </summary>
      /// <param name="solid">The solid.</param>
      /// <returns>The volume of the solid, or null if it can't be determined.</returns>
      public static double? GetSafeVolume(Solid solid)
      {
         try
         {
            return solid?.Volume ?? null;
         }
         catch
         {
            return null;
         }
      }

      /// <summary>
      /// Creates IFC curve from curve loop.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="curveLoop">The curve loop.</param>
      /// <param name="lcs">The local coordinate system whose XY plane the curves are projected on.</param>
      /// <param name="projDir">The project direction.</param>
      /// <returns>The created curve.</returns>
      public static IFCAnyHandle CreateIFCCurveFromCurveLoop(ExporterIFC exporterIFC, CurveLoop curveLoop, Transform lcs, XYZ projDir)
      {
         IFCFile file = exporterIFC.GetFile();

         List<IFCAnyHandle> segments = new List<IFCAnyHandle>();
         List<UV> polylinePts = new List<UV>(); // for simple case

         if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
         {
            return CreatePolyCurveFromCurveLoop(exporterIFC, curveLoop.ToList(), lcs, projDir);
         }
         else
         {
            bool useSimpleBoundary = false;
            if (!AllowComplexBoundary(lcs.BasisZ, projDir, curveLoop, null))
               useSimpleBoundary = true;

            foreach (Curve curve in curveLoop)
            {
               bool success = ProcessCurve(exporterIFC, curve, lcs, projDir, useSimpleBoundary,
                polylinePts, segments);
               if (!success)
                  return null;
            }

            bool needToClose = false;
            if (useSimpleBoundary)
            {
               int sz = polylinePts.Count;
               if (sz < 2)
                  return null;

               // Original CurveLoop may have been is closed.
               // Only remove the last point in polylinePoints if it really is the same as the first.
               if (!curveLoop.IsOpen())
               {
                  if (polylinePts[0].IsAlmostEqualTo(polylinePts[polylinePts.Count - 1]))
                  {
                     polylinePts.RemoveAt(sz - 1);
                  }
                  needToClose = true;
               }
            }

            return CreateCurveFromComponents(file, useSimpleBoundary, needToClose, polylinePts, segments);
         }
      }

      /// <summary>
      /// Creates IFC curve from curve array.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="curves">The curves.</param>
      /// <param name="lcs">The local coordinate system whose XY plane the curves are projected on.</param>
      /// <param name="projDir">The project direction.</param>
      /// <returns>The created curve.</returns>
      public static IFCAnyHandle CreateIFCCurveFromCurves(ExporterIFC exporterIFC, IList<Curve> curves, Transform lcs, XYZ projDir)
      {
         IFCFile file = exporterIFC.GetFile();

         List<IFCAnyHandle> segments = new List<IFCAnyHandle>();
         List<UV> polylinePts = new List<UV>(); // for simple case

         bool useSimpleBoundary = false;
         if (!AllowComplexBoundary(lcs.BasisZ, projDir, null, curves))
            useSimpleBoundary = true;

         foreach (Curve curve in curves)
         {
            bool success = ProcessCurve(exporterIFC, curve, lcs, projDir, useSimpleBoundary,
             polylinePts, segments);
            if (!success)
               return null;
         }

         bool needToClose = false;
         if (useSimpleBoundary)
         {
            int polySz = polylinePts.Count;
            if (polySz > 2)
            {
               if (MathUtil.IsAlmostEqual(polylinePts[0][0], polylinePts[polySz - 1][0]) && MathUtil.IsAlmostEqual(polylinePts[0][1], polylinePts[polySz - 1][1]))
               {
                  needToClose = true;
                  polylinePts.RemoveAt(polySz - 1);
               }
            }
         }

         return CreateCurveFromComponents(file, useSimpleBoundary, needToClose, polylinePts, segments);
      }

      /// <summary>
      /// Gets polyline points or curve segments from a curve.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="curve">The curve.</param>
      /// <param name="lcs">The local coordinate system whose XY vectors represent the plane on which the curve is projected.</param>
      /// <param name="projectDir">The project direction.</param>
      /// <param name="useSimpleBoundary">True if to create tessellated curve, false to create segments.</param>
      /// <param name="polylinePoints">The polyline points get from the curve.</param>
      /// <param name="curveSegments">The curve segments get from the curve.</param>
      /// <returns>True if process successfully.</returns>
      public static bool ProcessCurve(ExporterIFC exporterIFC, Curve curve, Transform lcs, XYZ projectDir, bool useSimpleBoundary,
          List<UV> polylinePoints, List<IFCAnyHandle> curveSegments)
      {
         IFCFile file = exporterIFC.GetFile();

         bool exportAs2x2 = ExporterCacheManager.ExportOptionsCache.ExportAs2x2;

         if (!useSimpleBoundary)
         {
            XYZ zDir = lcs.BasisZ;
            if (exportAs2x2 && !MathUtil.IsAlmostEqual(Math.Abs(zDir.DotProduct(projectDir)), 1.0))
            {
               useSimpleBoundary = true;
            }
         }

         if (useSimpleBoundary)
         {
            IList<UV> currPts = new List<UV>();

            IList<XYZ> points = curve.Tessellate();
            foreach (XYZ point in points)
            {
               UV projectPoint = GeometryUtil.ProjectPointToXYPlaneOfLCS(lcs, projectDir, point);
               if (projectPoint == null)
                  return false;
               currPts.Add(UnitUtil.ScaleLength(projectPoint));
            }

            // polylinePoints is accumulating projected points.
            // currPts are the points from the current curve.
            // If there are currently no points in polylinePoints --> just add all currPts to polylinePoints.
            if (polylinePoints.Count == 0)
            {
               polylinePoints.AddRange(currPts);
            }
            else
            {
               // Otherwise look at both ends of both collections.
               UV lastPolylinePoint = polylinePoints[polylinePoints.Count - 1];
               UV firstPolylinePoint = polylinePoints[0];
               UV lastCurrPt = currPts[currPts.Count - 1];
               UV firstCurrPt = currPts[0];

               if (lastPolylinePoint.IsAlmostEqualTo(firstCurrPt))
               {
                  currPts.RemoveAt(0);
                  polylinePoints.AddRange(currPts);
               }
               else if (lastPolylinePoint.IsAlmostEqualTo(lastCurrPt))
               {
                  currPts.Reverse();
                  currPts.RemoveAt(0);
                  polylinePoints.AddRange(currPts);
               }
               else if (firstPolylinePoint.IsAlmostEqualTo(firstCurrPt))
               {
                  currPts.Reverse();
                  polylinePoints.RemoveAt(0);
                  polylinePoints.InsertRange(0, currPts);
               }
               else if (firstPolylinePoint.IsAlmostEqualTo(lastCurrPt))
               {
                  polylinePoints.RemoveAt(0);
                  polylinePoints.InsertRange(0, currPts);
               }
               else
               {
                  // Default:  just add currPts to the end.
                  polylinePoints.AddRange(currPts);
               }
            }
         }
         else
         {
            IFCGeometryInfo info = IFCGeometryInfo.CreateCurveGeometryInfo(exporterIFC, lcs, projectDir, false);
            ExporterIFCUtils.CollectGeometryInfo(exporterIFC, info, curve, XYZ.Zero, false);
            IList<IFCAnyHandle> curves = info.GetCurves();
            if (curves.Count != 1 || !IFCAnyHandleUtil.IsSubTypeOf(curves[0], IFCEntityType.IfcBoundedCurve))
               return false;

            IFCAnyHandle boundedCurve = curves[0];

            bool mustFlip = MustFlipCurve(lcs, curve);
            curveSegments.Add(IFCInstanceExporter.CreateCompositeCurveSegment(file, IFCTransitionCode.Continuous, !mustFlip, boundedCurve));
         }

         return true;
      }

      static UV ScaledUVListFromXYZ(XYZ thePoint, Transform lcs, XYZ projectDir)
      {
         UV projectPoint = GeometryUtil.ProjectPointToXYPlaneOfLCS(lcs, projectDir, thePoint);
         return UnitUtil.ScaleLength(projectPoint);
      }

      private static double DistanceSquaredBetweenVertices(XYZ coord1, XYZ coord2)
      {
         var dX = coord1.X - coord2.X;
         var dY = coord1.Y - coord2.Y;
         var dZ = coord1.Z - coord2.Z;

         return dX * dX + dY * dY + dZ * dZ;
      }
      private static double DistanceSquaredBetweenVertices(UV coord1, UV coord2)
      {
         var dU = coord1.U - coord2.U;
         var dV = coord1.V - coord2.V;

         return dU * dU + dV * dV;
      }

      private static bool CoordsAreWithinVertexTol(PointBase coord1, PointBase coord2)
      {
         double vertexTol = UnitUtil.ScaleLength(ExporterCacheManager.Document.Application.VertexTolerance);
         Point3D coord13D = coord1 as Point3D;
         Point3D coord23D = coord2 as Point3D;
         if (coord13D != null && coord23D != null)
            return (DistanceSquaredBetweenVertices(coord13D.coords, coord23D.coords) < vertexTol * vertexTol);
         Point2D coord12D = coord1 as Point2D;
         Point2D coord22D = coord2 as Point2D;
         if (coord12D != null && coord22D != null)
            return (DistanceSquaredBetweenVertices(coord12D.coords, coord22D.coords) < vertexTol * vertexTol);

         throw new ArgumentException("Invalid Point type");
      }
      private static bool CoordsAreWithinVertexTol(XYZ coord1, XYZ coord2)
      {
         double vertexTol = UnitUtil.ScaleLength(ExporterCacheManager.Document.Application.VertexTolerance);
         return (DistanceSquaredBetweenVertices(coord1, coord2) < vertexTol * vertexTol);
      }
      private static bool CoordsAreWithinVertexTol(UV coord1, UV coord2)
      {
         double vertexTol = UnitUtil.ScaleLength(ExporterCacheManager.Document.Application.VertexTolerance);
         return (DistanceSquaredBetweenVertices(coord1, coord2) < vertexTol * vertexTol);
      }

      /// <summary>
      /// Create IFC4 IfcIndexedPolyCurve from Revit Curve
      /// </summary>
      /// <param name="exporterIFC">the ExporterIFC</param>
      /// <param name="curve">the Revit curve</param>
      /// <param name="lcs">Transform for the LCS (default=null)</param>
      /// <param name="projectDir">Projection direction (default=null)</param>
      /// <returns>IFCAnyHandle for the created IfcIndexedPolyCurve</returns>
      public static IFCAnyHandle CreatePolyCurveFromCurve(ExporterIFC exporterIFC, Curve curve, Transform lcs = null, XYZ projectDir = null)
      {
         IFCFile file = exporterIFC.GetFile();

         PrimVertices vertices = PointListFromCurve(exporterIFC, curve, lcs, projectDir);
         // Points from the curve may have been merged after projection, so skip curves that
         // won't add any new points.
         if (vertices == null)
            return null;

         PolyCurve polyCurve = new PolyCurve(vertices);
         polyCurve.BuildVerticesAndIndices();
         IList<SegmentIndices> segmentsIndices = polyCurve.SegmentsIndices;

         IFCAnyHandle pointListHnd = IFCInstanceExporter.CreateCartesianPointList(file, polyCurve.PointList);
         return IFCInstanceExporter.CreateIndexedPolyCurve(file, pointListHnd, segmentsIndices, false);
      }

      /// <summary>
      /// Create a IFC4 IfcIndexedPolyCurve from a Revit CurveLoop
      /// </summary>
      /// <param name="exporterIFC">The exporterIFC context.</param>
      /// <param name="curves">Curves.</param>
      /// <param name="lcs">The local coordinate system transform.</param>
      /// <param name="projectDir">The projection direction.</param>
      /// <returns>The IfcIndexedPolyCurve handle, or null if it couldn't be created.</returns>
      public static IFCAnyHandle CreatePolyCurveFromCurveLoop(ExporterIFC exporterIFC, IList<Curve> curves,
         Transform lcs, XYZ projectDir)
      {
         if (curves.Count() == 0)
            return null;

         IFCFile file = exporterIFC.GetFile();

         PolyCurve polyCurve = null;
         foreach (Curve curve in curves)
         {
            PrimVertices vertices = PointListFromCurve(exporterIFC, curve, lcs, projectDir);
            // Points from the curve may have been merged after projection, so skip curves that
            // won't add any new points.
            if (vertices == null)
               continue;

            if (polyCurve == null)
               polyCurve = new PolyCurve(vertices);
            else if (!polyCurve.AddPrimVertices(vertices))
               return null;
         }

         polyCurve.BuildVerticesAndIndices();
         IList<SegmentIndices> segmentsIndices = polyCurve.SegmentsIndices;

         IFCAnyHandle pointListHnd = IFCInstanceExporter.CreateCartesianPointList(file, polyCurve.PointList);
         return IFCInstanceExporter.CreateIndexedPolyCurve(file, pointListHnd, segmentsIndices, false);
      }

      private static PrimVertices PointListFromCurve(ExporterIFC exporterIFC, Curve curve,
         Transform lcs, XYZ projectDir)
      {
         if (curve == null)
            return null;

         if (curve is Line)
            return PointListFromLine(exporterIFC, curve as Line, lcs, projectDir);

         if (curve is Arc)
            return PointListFromArc(exporterIFC, curve as Arc, lcs, projectDir);

         return PointListFromGenericCurve(exporterIFC, curve, lcs, projectDir);
      }

      private static PolyLineVertices PointListFromLine(ExporterIFC exporterIFC, Line line,
         Transform lcs, XYZ projectDir)
      {
         bool use3DPoint = (lcs == null || projectDir == null);

         if (use3DPoint)
         {
            XYZ startPoint = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, line.GetEndPoint(0));
            XYZ endPoint = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, line.GetEndPoint(1));
            // Avoid consecutive duplicates
            if (CoordsAreWithinVertexTol(startPoint, endPoint))
               return null;

            return new PolyLineVertices(startPoint, endPoint);
         }
         else
         {
            var startPoint = ScaledUVListFromXYZ(line.GetEndPoint(0), lcs, projectDir);
            var endPoint = ScaledUVListFromXYZ(line.GetEndPoint(1), lcs, projectDir);
            // Avoid consecutive duplicates
            if (CoordsAreWithinVertexTol(startPoint, endPoint))
               return null;

            return new PolyLineVertices(startPoint, endPoint);
         }
      }

      private static PrimVertices PointListFromArc(ExporterIFC exporterIFC, Arc arc,
         Transform lcs, XYZ projectDir)
      {
         bool use3DPoint = (lcs == null || projectDir == null);

         if (use3DPoint && arc.IsBound)
         {
            XYZ startPoint = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, arc.Evaluate(0, true));
            XYZ midPoint = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, arc.Evaluate(0.5, true));
            XYZ endPoint = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, arc.Evaluate(1, true));

            return new ArcVertices(startPoint, midPoint, endPoint);
         }
         else
         {
            //If arc's normal is parallel to projection direction and Z axis of lcs then projected arc will be of correct shape.
            //That is why it can be defined by 3 points: start, mid, end.
            if (MathUtil.VectorsAreParallel(arc.Normal, projectDir) && MathUtil.VectorsAreParallel(lcs.BasisZ, projectDir) && arc.IsBound)
            {
               var startPoint = ScaledUVListFromXYZ(arc.Evaluate(0, true), lcs, projectDir);
               var midPoint = ScaledUVListFromXYZ(arc.Evaluate(0.5, true), lcs, projectDir);
               var endPoint = ScaledUVListFromXYZ(arc.Evaluate(1, true), lcs, projectDir);

               return new ArcVertices(startPoint, midPoint, endPoint);
            }

            //Handle cases when Arc is a circle (arc.IsBound == false) or projected arc is of incorrect shape
            //and cannot be defined by 3 points. Tesselate it in this case.
            List<XYZ> pointList3D = new List<XYZ>();
            List<UV> pointList2D = new List<UV>();

            // An integer value is used here to get an accurate interval the value ranges from
            // 0 to 90 or 100 percent, depending on whether the arc is bound or not.
            int normalizedEnd = arc.IsBound ? 10 : 9;
            XYZ lastPoint3D = null;
            UV lastPoint2D = null;
            for (int ii = 0; ii <= normalizedEnd; ++ii)
            {
               XYZ tessellationPt = arc.Evaluate(ii / 10.0, arc.IsBound);
               if (use3DPoint)
               {
                  XYZ point = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, tessellationPt);

                  // Avoid consecutive duplicates
                  if (lastPoint3D == null || !CoordsAreWithinVertexTol(point, lastPoint3D))
                  {
                     pointList3D.Add(point);
                     lastPoint3D = point;
                  }
               }
               else
               {
                  var point2D = ScaledUVListFromXYZ(tessellationPt, lcs, projectDir);

                  // Avoid consecutive duplicates
                  if (lastPoint2D == null || !CoordsAreWithinVertexTol(point2D, lastPoint2D))
                  {
                     pointList2D.Add(point2D);
                     lastPoint2D = point2D;
                  }
               }
            }

            if (pointList3D != null && pointList3D.Count >= 2)
               return new PolyLineVertices(pointList3D);
            else if (pointList2D != null && pointList2D.Count >= 2)
               return new PolyLineVertices(pointList2D);
         }

         return null;
      }

      private static PolyLineVertices PointListFromGenericCurve(ExporterIFC exporterIFC,
         Curve curve, Transform lcs, XYZ projectDir)
      {
         bool use3DPoint = (lcs == null || projectDir == null);

         List<XYZ> listXYZ = new List<XYZ>();
         List<UV> listUV = new List<UV>();

         IList<XYZ> tessellatedCurve = curve.Tessellate();
         var pointsCount = tessellatedCurve.Count;
         for (int ii = 0; ii < pointsCount; ++ii)
         {
            if (use3DPoint)
            {
               var point = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, tessellatedCurve[ii]);
               // Avoid consecutive duplicates
               if (listXYZ.Count == 0 || !CoordsAreWithinVertexTol(point, listXYZ.Last()))
                  listXYZ.Add(point);
            }
            else
            {
               var point = ScaledUVListFromXYZ(tessellatedCurve[ii], lcs, projectDir);
               // Avoid consecutive duplicates
               if (listUV.Count == 0 || !CoordsAreWithinVertexTol(point, listUV.Last()))
                  listUV.Add(point);
            }
         }

         if (listXYZ != null && listXYZ.Count >= 2)
            return new PolyLineVertices(listXYZ);
         else if (listUV != null && listUV.Count >= 2)
            return new PolyLineVertices(listUV);

         return null;
      }

      static private bool AllowedCurveForAllowComplexBoundary(Curve curve)
      {
         if (curve == null)
            return false;

         if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            return true;

         return ((curve is Line) || (curve is Arc) || (curve is Ellipse));
      }

      /// <summary>
      /// Checks if complex boundary is allowed for the CurveLoop or the curve array. 
      /// </summary>
      /// <param name="zDir">The normal of the plane to project on.</param>
      /// <param name="projDir">The project direction.</param>
      /// <param name="curveLoop">The curve loop.</param>
      /// <param name="curves">The curve array.</param>
      /// <returns>True if complex boundary is allowed.</returns>
      static bool AllowComplexBoundary(XYZ zDir, XYZ projDir, CurveLoop curveLoop, IList<Curve> curves)
      {
         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x2 && !MathUtil.IsAlmostEqual(Math.Abs(zDir.DotProduct(projDir)), 1.0))
            return false;

         // Checks below are for IFC2x3 or earlier only.
         if (curveLoop != null)
         {
            bool allLines = true;

            foreach (Curve curve in curveLoop)
            {
               if (!AllowedCurveForAllowComplexBoundary(curve))
                  return false;
               if (!(curve is Line))
                  allLines = false;
            }

            if (allLines)
               return false;
         }

         if (curves != null)
         {
            bool allLines = true;

            foreach (Curve curve in curves)
            {
               if (!AllowedCurveForAllowComplexBoundary(curve))
                  return false;
               if (!(curve is Line))
                  allLines = false;
            }

            if (allLines)
               return false;

            if (allLines)
               return false;
         }

         return true;
      }

      /// <summary>
      /// Creates an IFC curve from polyline points or curve segments.
      /// </summary>
      /// <param name="file">The file.</param>
      /// <param name="useSimpleBoundary">True to use simple boundary.</param>
      /// <param name="needToClose">True if the curve needs to be close.</param>
      /// <param name="pts">The polyline points.</param>
      /// <param name="segments">The curve segments.</param>
      /// <param mame="filterDuplicates">Filter duplicate points.  Example use case:  curves projected </param>
      /// <returns>The created IFC curve.</returns>
      static IFCAnyHandle CreateCurveFromComponents(IFCFile file, bool useSimpleBoundary, bool needToClose, IList<UV> pts, IList<IFCAnyHandle> segments)
      {
         IFCAnyHandle profileCurve;

         if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
         {
            IList<IList<double>> coords = new List<IList<double>>();
            foreach (UV pt in pts)
            {
               IList<double> uvPoint = new List<double>();
               uvPoint.Add(pt.U);
               uvPoint.Add(pt.V);
               coords.Add(uvPoint);
            }
            if (coords.Count < 2)
               return null;

            IFCAnyHandle coordsHnd = IFCInstanceExporter.CreateCartesianPointList2D(file, coords);
            profileCurve = IFCInstanceExporter.CreateIndexedPolyCurve(file, coordsHnd, null, false);
            return profileCurve;
         }

         if (useSimpleBoundary)
         {
            int sz = pts.Count;
            if (sz < 2)
               return null;

            int uniqueUVFuzzyCompare(UV first, UV second)
            {
               if (first == null)
               {
                  return (second == null) ? 0 : -1;
               }

               if (second == null)
               {
                  return 1;
               }

               if (!first.IsAlmostEqualTo(second))
               {
                  double tolerance = ExporterCacheManager.LengthPrecision;
                  for (int ii = 0; ii < 2; ii++)
                  {
                     double diff = first[ii] - second[ii];
                     if (diff < -tolerance)
                        return -1;
                     if (diff > tolerance)
                        return 1;
                  }
               }

               return 0;
            }

            SortedSet<UV> uniqueSet = new(Comparer<UV>.Create(uniqueUVFuzzyCompare));
            List<UV> uniquePoints = new();
            foreach (UV pt in pts)
            {
               if (uniqueSet.Contains(pt))
                  continue;

               uniqueSet.Add(pt);
               uniquePoints.Add(pt);
            }

            IList<IFCAnyHandle> polyLinePts = new List<IFCAnyHandle>();
            foreach (UV pt in uniquePoints)
            {
               polyLinePts.Add(ExporterUtil.CreateCartesianPoint(file, pt));
            }

            if (needToClose)
               polyLinePts.Add(polyLinePts[0]);

            if (polyLinePts.Count < 2)
               return null;

            profileCurve = IFCInstanceExporter.CreatePolyline(file, polyLinePts);
         }
         else
         {
            profileCurve = IFCInstanceExporter.CreateCompositeCurve(file, segments, IFCLogical.False);
         }
         return profileCurve;
      }

      /// <summary>
      /// Function to process list of triangles set into an indexed triangles format for Tessellated geometry
      /// </summary>
      /// <param name="file">The IFC file.</param>
      /// <param name="triangleList">The list of triangles.</param>
      /// <returns>An IFC handle for an IfcTriangulatedFaceSet.</returns>
      public static IFCAnyHandle GetIndexedTriangles(IFCFile file, List<List<XYZ>> triangleList)
      {
         // Match the tolerance set in TriangleMergeUtil. 
         const double tolerance = TriangleMergeUtil.Tolerance;
         IDictionary<IFCFuzzyXYZ, int> vertexMap = new SortedDictionary<IFCFuzzyXYZ, int>();

         IList<IList<int>> triangleIndices = new List<IList<int>>();

         if (triangleList.Count == 0)
            return null;

         int count = 1;
         foreach (List<XYZ> triangle in triangleList)
         {
            // This is probably overkill since we expect triangle to have 3 entries.
            // However, noting actually ensures that, and this should be fast anyway.
            ISet<int> usedIndices = new HashSet<int>(3);
            bool addCurrent = true;

            // Create triangle index and insert the index list of 3 into the triangle index list
            List<int> currentTriangleIndices = new List<int>();
            foreach (XYZ vertex in triangle)
            {
               IFCFuzzyXYZ fuzzyVertex = new IFCFuzzyXYZ(vertex, tolerance);
               int index;
               if (!vertexMap.TryGetValue(fuzzyVertex, out index))
               {
                  // Point not found, insert the point into the list
                  vertexMap[fuzzyVertex] = count;
                  index = count++;
               }

               if (usedIndices.Contains(index))
               {
                  // Triangle has a 0 length side, within tolerance.  Don't add.
                  addCurrent = false;
                  break;
               }
               usedIndices.Add(index);

               //!!! The index starts at 1 (and not 0) following X3D standard
               currentTriangleIndices.Add(index);
            }

            if (addCurrent)
               triangleIndices.Add(currentTriangleIndices);
         }

         // Didn't add anything.
         int mapCount = vertexMap.Count;
         if (mapCount == 0 || triangleIndices.Count == 0)
            return null;

         List<IList<double>> coordList = new List<IList<double>>(mapCount);
         for (int ii = 0; ii < mapCount; ii++)
         {
            coordList.Add(new List<double>(3));
         }

         foreach (KeyValuePair<IFCFuzzyXYZ, int> vertexAndIndex in vertexMap)
         {
            IFCFuzzyXYZ vertex = vertexAndIndex.Key;
            int index = vertexAndIndex.Value - 1;
            coordList[index].Add(vertex.X);
            coordList[index].Add(vertex.Y);
            coordList[index].Add(vertex.Z);
         }

         IFCAnyHandle coordPointLists = IFCInstanceExporter.CreateCartesianPointList3D(file, coordList);

         IFCAnyHandle triangulatedItem = IFCInstanceExporter.CreateTriangulatedFaceSet(file, coordPointLists, null, null, triangleIndices, null);

         return triangulatedItem;
      }

      /// <summary>
      /// Check if two bounding boxes overlap. 
      /// </summary>
      /// <param name="originalBox1">The first bounding box.</param>
      /// <param name="originalBox2">The second bounding box.</param>
      /// <returns>True if originalBox1 overlaps with originalBox2.</returns>
      /// <remarks>
      /// If the given bounding boxes are transformed, then this function will create two 
      /// axes-aligned bounding boxes of these two boxes in the model coordinate system, 
      /// and then check if the two new bounding boxes.  This could result in false-positive
      /// results in some cases.
      /// </remarks>
      public static bool BoundingBoxesOverlap(BoundingBoxXYZ originalBox1, BoundingBoxXYZ originalBox2)
      {
         if ((originalBox1 == null || !originalBox1.Enabled || originalBox2 == null || !originalBox2.Enabled))
            return false;

         BoundingBoxXYZ bbox1 = BoundingBoxInModelCoordinate(originalBox1);
         BoundingBoxXYZ bbox2 = BoundingBoxInModelCoordinate(originalBox2);

         if (bbox1 == null || bbox2 == null)
            return false;

         // We want bbox1 to be such that for all of X, Y and Z, either
         // min or max is inside the bbox2 range.
         return (bbox1.Max.X >= bbox2.Min.X) && (bbox1.Min.X <= bbox2.Max.X)
             && (bbox1.Max.Y >= bbox2.Min.Y) && (bbox1.Min.Y <= bbox2.Max.Y)
             && (bbox1.Max.Z >= bbox2.Min.Z) && (bbox1.Min.Z <= bbox2.Max.Z);
      }

      // return the bounding box in model coordinate of the given box
      private static BoundingBoxXYZ BoundingBoxInModelCoordinate(BoundingBoxXYZ bbox)
      {
         if (bbox == null)
            return null;

         double[] xVals = new double[] { bbox.Min.X, bbox.Max.X };
         double[] yVals = new double[] { bbox.Min.Y, bbox.Max.Y };
         double[] zVals = new double[] { bbox.Min.Z, bbox.Max.Z };

         XYZ toTest;

         double minX, minY, minZ, maxX, maxY, maxZ;
         minX = minY = minZ = double.MaxValue;
         maxX = maxY = maxZ = double.MinValue;

         // Get the max and min coordinate from the 8 vertices
         for (int iX = 0; iX < 2; iX++)
         {
            for (int iY = 0; iY < 2; iY++)
            {
               for (int iZ = 0; iZ < 2; iZ++)
               {
                  toTest = bbox.Transform.OfPoint(new XYZ(xVals[iX], yVals[iY], zVals[iZ]));
                  minX = Math.Min(minX, toTest.X);
                  minY = Math.Min(minY, toTest.Y);
                  minZ = Math.Min(minZ, toTest.Z);

                  maxX = Math.Max(maxX, toTest.X);
                  maxY = Math.Max(maxY, toTest.Y);
                  maxZ = Math.Max(maxZ, toTest.Z);
               }
            }
         }

         BoundingBoxXYZ returnBox = new BoundingBoxXYZ();
         returnBox.Max = new XYZ(maxX, maxY, maxZ);
         returnBox.Min = new XYZ(minX, minY, minZ);

         return returnBox;
      }

      /// <summary>
      /// Organizes the edge loops of a face in groups of one outer loop and its corresponding inner loops.
      /// </summary>
      /// <param name="face">The input face</param>
      /// <returns></returns>
      public static List<(EdgeArray outerLoop, List<EdgeArray> innerLoops)> GetOuterLoopsWithInnerLoops(Face face)
      {
         var uvDomain = face.GetBoundingBox();

         var outerLoops = new List<EdgeArray>();
         var innerLoops = new List<EdgeArray>();
         var combinedLoops = new List<(EdgeArray outerLoop, List<EdgeArray> innerLoops)>();

         //Classify as outer or inner loop by looking at loop orientations.
         foreach (var loop in face.EdgeLoops.Cast<EdgeArray>())
         {
            if (LoopIsCCWOnFace(face, loop))
               outerLoops.Add(loop);
            else
               innerLoops.Add(loop);
         }

         //Special cases of no inner loops or only one outer loop.
         if (!innerLoops.Any())
            return outerLoops.Select(ol => (ol, new List<EdgeArray>())).ToList();

         if (outerLoops.Count == 1)
            return new List<(EdgeArray outerLoop, List<EdgeArray> innerLoops)> { (outerLoops[0], innerLoops) };

         //Special case where the outer loop has incorrect orientation. Still try to export with single outer loop.
         if (outerLoops.Count == 0 && innerLoops.Count == 1)
            return new List<(EdgeArray outerLoop, List<EdgeArray> innerLoops)> { (innerLoops[0], outerLoops) };

         //No special case, have to find out which inner loop belongs to which outer loop.
         //We do this by sampling the outer loops with rather high accuracy to approximate them
         //with polygons and by checking for containment of a point of the inner loop in one 
         //of those polygons.
         var outerLoopToSamples = new Dictionary<EdgeArray, IList<UV>>();

         var result = outerLoops.Select(ol => (ol, new List<EdgeArray>())).ToList();

         foreach (var innerLoop in innerLoops)
         {
            var uv = innerLoop.Cast<Edge>().First().EvaluateOnFace(0.0, face);

            bool found = false;
            foreach (var (outerLoop, innerLoopsForOuter) in result)
            {
               IList<UV> samples = null;
               if (!outerLoopToSamples.TryGetValue(outerLoop, out samples))
               {
                  samples = TessellateLoopOnFace(face, outerLoop);
                  outerLoopToSamples[outerLoop] = samples;
               }

               if (PointInsidePolygon(uv, samples))
               {
                  innerLoopsForOuter.Add(innerLoop);
                  found = true;
                  break;
               }
            }

            if (!found)
               return null;
         }
         return result;
      }

      /// <summary>
      /// Determines wether a given loop on a face is CCW w.r.t. the face normal.
      /// </summary>
      /// <param name="face">The input face</param>
      /// <param name="loop">The input loop on the face</param>
      /// <returns></returns>
      private static bool LoopIsCCWOnFace(Face face, EdgeArray loop)
      {
         var uvSamples = SampleLoopOnFaceSimple(loop, face, 4);

         var points = uvSamples.Select(uv => new XYZ(uv.U, uv.V, 0.0)).ToList();
         var normal = TriangleMergeUtil.NormalByNewellMethod(points);
         bool ccwAroundSurfaceNormal = normal.Z > 0.0;
         return face.OrientationMatchesSurfaceOrientation ? !ccwAroundSurfaceNormal : ccwAroundSurfaceNormal;
      }

      /// <summary>
      /// Does simple sampling of an edge loop on a face.
      /// </summary>
      /// <param name="loop">The input loop</param>
      /// <param name="face">The face on which the loop resides</param>
      /// <param name="nSamplesPerCurvedEdge">The number of samples, not including the end point, that is taken on a curved edge</param>
      /// <returns></returns>
      private static UV[] SampleLoopOnFaceSimple(EdgeArray loop, Face face, int nSamplesPerCurvedEdge)
      {
         return loop.Cast<Edge>().SelectMany(e => SampleEdgeOnFaceSimple(e, face, nSamplesPerCurvedEdge)).ToArray();
      }

      /// <summary>
      /// Does simple sampling of an edge on a face.
      /// </summary>
      /// <param name="edge">The input edge</param>
      /// <param name="face">The face to which the edge belongs</param>
      /// <param name="nSamplesWhenCurved">The number of samples, not including the end point, that is taken on a curved edge</param>
      /// <returns></returns>
      private static UV[] SampleEdgeOnFaceSimple(Edge edge, Face face, int nSamplesWhenCurved)
      {
         int nSamples = (edge.AsCurve() is Line) ? 1 : nSamplesWhenCurved;

         double step = 1.0 / (double)nSamples;

         var sampleParams = new List<double> { };

         (int iStart, int iEnd, int increment) = edge.IsFlippedOnFace(face) ? (nSamples, 0, -1) : (0, nSamples, 1);
         for (int i = iStart; i != iEnd; i += increment)
            sampleParams.Add(i * step);

         return sampleParams.Select(p => edge.EvaluateOnFace(p, face)).ToArray();
      }

      /// <summary>
      /// Tessellates a loop on a face using built in accuracy parameters that are adequate for visual representation.
      /// The UV samples are ordered such that the loop is traversed in its natural way
      /// i.e. CCW around the face normal for outer loops and CW for inner loops.
      /// </summary>
      /// <param name="face">The face of the loop</param>
      /// <param name="loop">The loop</param>
      /// <returns></returns>
      private static IList<UV> TessellateLoopOnFace(Face face, EdgeArray loop)
      {
         return loop.Cast<Edge>().SelectMany(e =>
         {
            var uvs = e.TessellateOnFace(face);

            return e.IsFlippedOnFace(face) ? uvs.Reverse() : uvs;
         }).ToList();
      }

      /// <summary>
      /// Check if the given loop (loopToCheck) is inside any of the loop in the given list of loops (listOfLoops). 
      /// The loop that contains loopToCheck will be stored in outerLoop
      /// Currently this method is only used in SortEdgeLoop, and we are sure that there is at most one loop that can contain
      /// loopToCheck. If there is no such loop, then outerLoop will be an empty list
      /// </summary>
      /// <param name="loopToCheck">The given loop</param>
      /// <param name="listOfLoops">The given list of loops</param>
      /// <returns>true if the given loop is inside any of the loop in the given list of loops</returns>
      private static bool IsInsideAnotherLoop(IList<UV> loopToCheck, IList<IList<UV>> listOfLoops)
      {
         foreach (IList<UV> loop in listOfLoops)
         {
            if (PointInsidePolygon(loopToCheck[0], loop))
            {
               return true;
            }
         }
         return false;
      }

      /// <summary>
      /// Check if the given loop (loopToCheck) is outside any of the loop in the given list of loops (listOfLoops).
      /// Every loop that is inside loopToCheck will be collected and stored in resultedList
      /// </summary>
      /// <param name="loopToCheck">The given loop</param>
      /// <param name="listOfLoops">The given list of loops</param>
      /// <param name="resultedList">The list of loops that is inside loopToCheck</param>
      /// <returns>true if loopToCheck is outside any loop in the given list of loops</returns>
      private static bool IsOutsideOtherLoops(IList<UV> loopToCheck, IList<IList<UV>> listOfLoops, out IList<IList<UV>> resultedList)
      {
         resultedList = new List<IList<UV>>();
         foreach (IList<UV> loop in listOfLoops)
         {
            if (PointInsidePolygon(loop[0], loopToCheck))
            {
               resultedList.Add(loop);
            }
         }

         return resultedList.Count > 0;
      }

      /// <summary>
      /// Checks if the given point is inside the given loop
      /// </summary>
      /// <param name="pnt">The given point</param>
      /// <param name="polyNodes">The given loop</param>
      /// <returns>true if the given point is inside the given loop</returns>
      /// <remarks>This function returns an arbitrary result when the point is on the boundary of the polygon. The caller of this function should check
      ///          if the point is on the boundary first</remarks>
      private static bool PointInsidePolygon(UV pnt, IList<UV> polyNodes)
      {
         if (pnt == null || polyNodes == null || polyNodes.Count == 0)
            return false;

         int nNodes = polyNodes.Count;
         // Find the number of intersections of the ray strting from the 'pnt'
         // in the left direction, with the edges of the polygon.
         int count = 0; // number of intersections
         for (int iPrev = 0; iPrev < nNodes; iPrev++)
         {
            int iNext = (iPrev + 1) % nNodes;
            if (polyNodes[iPrev].V >= pnt.V == polyNodes[iNext].V < pnt.V)
            {
               if ((pnt.V - polyNodes[iPrev].V) * (polyNodes[iPrev].U - polyNodes[iNext].U) /
                  (polyNodes[iPrev].V - polyNodes[iNext].V) + polyNodes[iPrev].U < pnt.U)
                  count++;
            }
         }
         return (count % 2) != 0;
      }

      /// <summary>
      /// Create IFCCurve from the given curve
      /// </summary>
      /// <param name="file">The file</param>
      /// <param name="exporterIFC">The exporter</param>
      /// <param name="curve">The curve that needs to convert to IFCCurve</param>
      /// <param name="allowAdvancedCurve">If true, don't tessellate non-lines and non-arcs.</param>
      /// <param name="cartesianPoints">A map of already created cartesian points, to avoid duplication.</param>
      /// <param name="trimCurvePreference">An indication of how to create the curve.</param>
      /// <returns>The handle representing the IFCCurve</returns>
      /// <remarks>This cartesianPoints map caches certain 3D points computed by this function that are related to the 
      /// curve, such as the start point of a line and the center of an arc.  It uses the cached values when possible.</remarks>
      public static IFCAnyHandle CreateIFCCurveFromRevitCurve(IFCFile file, 
         ExporterIFC exporterIFC, Curve curve, bool allowAdvancedCurve,
         IDictionary<IFCFuzzyXYZ, IFCAnyHandle> cartesianPoints,
         TrimCurvePreference trimCurvePreference, Transform additionalTrf)
      {
         IFCAnyHandle ifcCurve = null;

         if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
         {
            PrimVertices vertices = PointListFromCurve(exporterIFC, curve, additionalTrf, null);
            // Points from the curve may have been merged after projection, so skip curves that
            // won't add any new points.
            if (vertices == null)
               return null;

            PolyCurve polyCurve = new PolyCurve(vertices);
            polyCurve.BuildVerticesAndIndices();
            IList<SegmentIndices> segmentsIndices = polyCurve.SegmentsIndices;

            IFCAnyHandle pointListHnd = IFCInstanceExporter.CreateCartesianPointList(file, polyCurve.PointList);
            return IFCInstanceExporter.CreateIndexedPolyCurve(file, pointListHnd, segmentsIndices, false);
         }

         // if the Curve is a line, do the following
         if (curve is Line)
         {
            // Unbounded line doesn't make sense, skip if somehow it is 
            if (curve.IsBound)
            {
               Line curveLine = curve as Line;
               if (trimCurvePreference == TrimCurvePreference.UsePolyLineOrTrim)
               {
                  ifcCurve = CreateLineSegment(exporterIFC, curveLine);
               }
               else
               {
                  // Create line based trimmed curve for Axis
                  IFCAnyHandle curveOrigin = XYZtoIfcCartesianPoint(exporterIFC, curveLine.Origin, cartesianPoints, additionalTrf);
                  XYZ dir = (additionalTrf == null) ? curveLine.Direction : additionalTrf.OfVector(curveLine.Direction);
                  IFCAnyHandle vector = VectorToIfcVector(exporterIFC, dir);
                  ifcCurve = IFCInstanceExporter.CreateLine(file, curveOrigin, vector);

                  if (trimCurvePreference == TrimCurvePreference.TrimmedCurve)
                  {
                     IFCAnyHandle startPoint = XYZtoIfcCartesianPoint(exporterIFC, curveLine.GetEndPoint(0), cartesianPoints, additionalTrf);
                     HashSet<IFCData> trim1 = new HashSet<IFCData>() { IFCData.CreateIFCAnyHandle(startPoint) };
                     IFCAnyHandle endPoint = XYZtoIfcCartesianPoint(exporterIFC, curveLine.GetEndPoint(1), cartesianPoints, additionalTrf);
                     HashSet<IFCData> trim2 = new HashSet<IFCData>() { IFCData.CreateIFCAnyHandle(endPoint) };

                     ifcCurve = IFCInstanceExporter.CreateTrimmedCurve(file, ifcCurve, trim1, trim2, true, IFCTrimmingPreference.Cartesian);
                  }
               }
            }
         }
         // if the Curve is an Arc do following
         else if (curve is Arc)
         {
            Arc curveArc = curve as Arc;
            double radius = UnitUtil.ScaleLength(curveArc.Radius);
            if (!IFCInstanceExporter.ValidateCircle(radius))
            {
               return null;
            }

            // Normal and x direction should be transformed to IFC coordinates before applying additional transform
            // arc center will be transformed later in XYZtoIfcCartesianPoint
            XYZ curveArcNormal = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, curveArc.Normal);
            XYZ curveArcXDirection = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, curveArc.XDirection);
            if (additionalTrf != null)
            {
               curveArcNormal = additionalTrf.OfVector(curveArcNormal);
               curveArcXDirection = additionalTrf.OfVector(curveArcXDirection);
            }

            if (curveArc.Center == null || curveArcNormal == null || curveArcXDirection == null)
            {
               // encounter invalid curve, return null
               return null;
            }
            IFCAnyHandle location3D = XYZtoIfcCartesianPoint(exporterIFC, curveArc.Center, cartesianPoints, additionalTrf);

            // Create the z-direction
            // No need to transform to IFC coordinates anymore
            IFCAnyHandle axis = ExporterUtil.CreateDirection(file, curveArcNormal);

            // Create the x-direction
            // No need to transform to IFC coordinates anymore
            IFCAnyHandle refDirection = ExporterUtil.CreateDirection(file, curveArcXDirection);

            IFCAnyHandle position3D = IFCInstanceExporter.CreateAxis2Placement3D(file, location3D, axis, refDirection);
            ifcCurve = IFCInstanceExporter.CreateCircle(file, position3D, radius);

            if (trimCurvePreference != TrimCurvePreference.BaseCurve && curve.IsBound)
            {
               IFCAnyHandle startPoint = XYZtoIfcCartesianPoint(exporterIFC, curveArc.GetEndPoint(0), cartesianPoints, additionalTrf);
               HashSet<IFCData> trim1 = new HashSet<IFCData>() { IFCData.CreateIFCAnyHandle(startPoint) };

               IFCAnyHandle endPoint = XYZtoIfcCartesianPoint(exporterIFC, curveArc.GetEndPoint(1), cartesianPoints, additionalTrf);
               HashSet<IFCData> trim2 = new HashSet<IFCData>() { IFCData.CreateIFCAnyHandle(endPoint) };

               ifcCurve = IFCInstanceExporter.CreateTrimmedCurve(file, ifcCurve, trim1, trim2, true, IFCTrimmingPreference.Cartesian);
            }
         }
         // If curve is an ellipse or elliptical Arc type
         else if (curve is Ellipse)
         {
            Ellipse curveEllipse = curve as Ellipse;
            double semiAxis1 = UnitUtil.ScaleLength(curveEllipse.RadiusX);
            double semiAxis2 = UnitUtil.ScaleLength(curveEllipse.RadiusY);
            if (!IFCInstanceExporter.ValidateEllipse(semiAxis1, semiAxis2))
            {
               return null;
            }

            // Normal and x direction should be transformed to IFC coordinates before applying additional transform
            XYZ ellipseNormal = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, curveEllipse.Normal);
            XYZ ellipseXDirection = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, curveEllipse.XDirection);
            if (additionalTrf != null)
            {
               ellipseNormal = additionalTrf.OfVector(ellipseNormal);
               ellipseXDirection = additionalTrf.OfVector(ellipseXDirection);
            }

            IFCAnyHandle location3D = XYZtoIfcCartesianPoint(exporterIFC, curveEllipse.Center, cartesianPoints, additionalTrf);

            // No need to transform to IFC coordinates anymore
            IFCAnyHandle axis = ExporterUtil.CreateDirection(file, ellipseNormal);

            // Create the x-direction
            // No need to transform to IFC coordinates anymore
            IFCAnyHandle refDirection = ExporterUtil.CreateDirection(file, ellipseXDirection);

            IFCAnyHandle position = IFCInstanceExporter.CreateAxis2Placement3D(file, location3D, axis, refDirection);

            ifcCurve = IFCInstanceExporter.CreateEllipse(file, position, semiAxis1, semiAxis2);

            if (trimCurvePreference != TrimCurvePreference.BaseCurve && curve.IsBound)
            {
               IFCAnyHandle startPoint = XYZtoIfcCartesianPoint(exporterIFC, curveEllipse.GetEndPoint(0), cartesianPoints, additionalTrf);
               HashSet<IFCData> trim1 = new HashSet<IFCData>() { IFCData.CreateIFCAnyHandle(startPoint) };

               IFCAnyHandle endPoint = XYZtoIfcCartesianPoint(exporterIFC, curveEllipse.GetEndPoint(1), cartesianPoints, additionalTrf);
               HashSet<IFCData> trim2 = new HashSet<IFCData>() { IFCData.CreateIFCAnyHandle(endPoint) };

               ifcCurve = IFCInstanceExporter.CreateTrimmedCurve(file, ifcCurve, trim1, trim2, true, IFCTrimmingPreference.Cartesian);
            }
         }
         else if (allowAdvancedCurve && (curve is HermiteSpline || curve is NurbSpline))
         {
            NurbSpline nurbSpline = null;
            if (curve is HermiteSpline)
            {
               nurbSpline = NurbSpline.Create(curve as HermiteSpline);
               if (nurbSpline == null)
               {
                  throw new InvalidOperationException("Cannot convert this hermite spline to nurbs");
               }
            }
            else
            {
               nurbSpline = curve as NurbSpline;
            }

            int degree = nurbSpline.Degree;
            IList<XYZ> controlPoints = nurbSpline.CtrlPoints;
            IList<IFCAnyHandle> controlPointsInIfc = new List<IFCAnyHandle>();
            foreach (XYZ xyz in controlPoints)
            {
               controlPointsInIfc.Add(XYZtoIfcCartesianPoint(exporterIFC, xyz, cartesianPoints, additionalTrf));
            }

            // Based on IFC4 specification, curveForm is for information only, leave it as UNSPECIFIED for now.
            Revit.IFC.Export.Toolkit.IFC4.IFCBSplineCurveForm curveForm = Toolkit.IFC4.IFCBSplineCurveForm.UNSPECIFIED;

            IFCLogical closedCurve = nurbSpline.IsClosed ? IFCLogical.True : IFCLogical.False;

            // Based on IFC4 specification, selfIntersect is for information only, leave it as Unknown for now
            IFCLogical selfIntersect = IFCLogical.Unknown;

            // Unlike Revit, IFC uses 2 lists to store knots information. The first list contain every distinct knot, 
            // and the second list stores the multiplicities of each knot. The following code creates those 2 lists  
            // from the Knots property of Revit NurbSpline
            DoubleArray revitKnots = nurbSpline.Knots;
            IList<double> ifcKnots = new List<double>();
            IList<int> knotMultiplitices = new List<int>();

            foreach (double knot in revitKnots)
            {
               if (ifcKnots.Count == 0 || !MathUtil.IsAlmostEqual(knot, ifcKnots[ifcKnots.Count - 1]))
               {
                  ifcKnots.Add(knot);
                  knotMultiplitices.Add(1);
               }
               else
               {
                  knotMultiplitices[knotMultiplitices.Count - 1]++;
               }
            }

            // Based on IFC4 specification, knotSpec is for information only, leave it as UNSPECIFIED for now.
            Toolkit.IFC4.IFCKnotType knotSpec = Toolkit.IFC4.IFCKnotType.UNSPECIFIED;

            if (!nurbSpline.isRational)
            {
               ifcCurve = IFCInstanceExporter.CreateBSplineCurveWithKnots
                   (file, degree, controlPointsInIfc, curveForm, closedCurve, selfIntersect, knotMultiplitices, ifcKnots, knotSpec);
            }
            else
            {
               DoubleArray revitWeights = nurbSpline.Weights;
               IList<double> ifcWeights = new List<double>();

               foreach (double weight in revitWeights)
               {
                  ifcWeights.Add(weight);
               }

               ifcCurve = IFCInstanceExporter.CreateRationalBSplineCurveWithKnots
                   (file, degree, controlPointsInIfc, curveForm, closedCurve, selfIntersect, knotMultiplitices, ifcKnots, knotSpec, ifcWeights);
            }
         }
         // if the Curve is of any other type, tessellate it and use polyline to represent it
         else
         {
            // any other curve is not supported, we will tessellate it
            IList<XYZ> tessCurve = curve.Tessellate();
            IList<IFCAnyHandle> polylineVertices = new List<IFCAnyHandle>();
            foreach (XYZ vertex in tessCurve)
            {
               IFCAnyHandle ifcVert = XYZtoIfcCartesianPoint(exporterIFC, vertex, cartesianPoints);
               polylineVertices.Add(ifcVert);
            }
            ifcCurve = IFCInstanceExporter.CreatePolyline(file, polylineVertices);
         }
         return ifcCurve;
      }

      /// <summary>
      /// Converts the given XYZ point to IfcCartesianPoint3D
      /// </summary>
      /// <param name="exporterIFC">The exporter</param>
      /// <param name="thePoint">The point</param>
      /// <param name="cartesianPoints">A map of already created IfcCartesianPoints.  This argument may be null.</param>
      /// <returns>The handle representing IfcCartesianPoint</returns>
      public static IFCAnyHandle XYZtoIfcCartesianPoint(ExporterIFC exporterIFC, XYZ thePoint,
         IDictionary<IFCFuzzyXYZ, IFCAnyHandle> cartesianPoints, Transform additionalTrf = null)
      {
         IFCFile file = exporterIFC.GetFile();
         XYZ vertexScaled = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, thePoint);

         if (additionalTrf != null)
            vertexScaled = additionalTrf.OfPoint(vertexScaled);
         IFCFuzzyXYZ fuzzyVertexScaled = (cartesianPoints != null) ? new IFCFuzzyXYZ(vertexScaled) : null;

         IFCAnyHandle cartesianPoint = null;
         if (fuzzyVertexScaled != null && cartesianPoints.TryGetValue(fuzzyVertexScaled, out cartesianPoint))
            return cartesianPoint;

         cartesianPoint = ExporterUtil.CreateCartesianPoint(file, vertexScaled);
         if (fuzzyVertexScaled != null)
            cartesianPoints[fuzzyVertexScaled] = cartesianPoint;

         return cartesianPoint;
      }

      /// <summary>
      /// Converts the given XYZ vector to IfcDirection
      /// </summary>
      /// <param name="exporterIFC">The exporter</param>
      /// <param name="theVector">The vector</param>
      /// <returns>The hanlde representing IfcDirection</returns>
      public static IFCAnyHandle VectorToIfcDirection(ExporterIFC exporterIFC, XYZ theVector)
      {
         IFCFile file = exporterIFC.GetFile();
         XYZ vectorScaled = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, theVector);

         IFCAnyHandle direction = ExporterUtil.CreateDirection(file, vectorScaled);
         return direction;
      }

      /// <summary>
      /// Converts the given XYZ vector to IfcVector
      /// </summary>
      /// <param name="exporterIFC">The exporter</param>
      /// <param name="theVector">The vector</param>
      /// <returns>The hanlde representing IfcVector</returns>
      public static IFCAnyHandle VectorToIfcVector(ExporterIFC exporterIFC, XYZ theVector)
      {
         IFCFile file = exporterIFC.GetFile();
         XYZ vectorScaled = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, theVector);

         double lineLength = UnitUtil.ScaleLength(theVector.GetLength());

         IFCAnyHandle vector = ExporterUtil.CreateVector(file, vectorScaled, lineLength);
         return vector;
      }

      /// <summary>
      /// Try to create valid extrusion and extract its end faces.
      /// </summary>
      /// <param name="solid">the solid geometry</param>
      /// <param name="basePlane">base plane of the profile</param>
      /// <param name="planeOrigin">the plane origin</param>
      /// <param name="tryNonPerpendicularExtrusion">option to try non perpendicular extrusion</param>
      /// <param name="checkOrdinarity">option to accept only ordinarity extrusion</param>
      /// <param name="extrusionEndFaces">output extrusion end faces</param>
      /// <param name="faceBoundaries">output face boundaries</param>
      /// <returns>trues if valid extrusion can be created</returns>
      public static bool TryGetExtrusionEndFaces(Solid solid, Plane basePlane, XYZ planeOrigin, bool tryNonPerpendicularExtrusion,
         bool checkOrdinarity, out IList<Face> extrusionEndFaces, out IList<CurveLoop> faceBoundaries)
      {
         extrusionEndFaces = new List<Face>();
         faceBoundaries = new List<CurveLoop>();

         Plane extrusionAnalyzerPlane = CreatePlaneByXYVectorsContainingPoint(basePlane.XVec, basePlane.YVec, planeOrigin);
         ExtrusionAnalyzer elementAnalyzer = ExtrusionAnalyzer.Create(solid, extrusionAnalyzerPlane, basePlane.Normal);

         XYZ baseLoopOffset = null;

         if (!MathUtil.IsAlmostZero(elementAnalyzer.StartParameter))
            baseLoopOffset = elementAnalyzer.StartParameter * basePlane.Normal;

         Face extrusionBase = elementAnalyzer.GetExtrusionBase();

         // Ensure there are only 2 unaligned faces and all the rest must be fully aligned
         IDictionary<Face, ExtrusionAnalyzerFaceAlignment> allFaces = elementAnalyzer.CalculateFaceAlignment();
         IList<Face> fullyAlignedFaces = new List<Face>();
         IList<Face> candidateEndFaces = new List<Face>();
         foreach (KeyValuePair<Face, ExtrusionAnalyzerFaceAlignment> item in allFaces)
         {
            if (item.Value == ExtrusionAnalyzerFaceAlignment.FullyAligned)
            {
               // For ordinary extrusion, there will be no unaligned faces. The end faces of extrusion should be fully aligned. 
               //   The idetification will be based on their normal = the extrusion base plane normal
               if (!tryNonPerpendicularExtrusion
                  && (item.Key.ComputeNormal(UV.Zero).IsAlmostEqualTo(basePlane.Normal) || item.Key.ComputeNormal(UV.Zero).IsAlmostEqualTo(basePlane.Normal.Negate())))
                  candidateEndFaces.Add(item.Key);
               else
                  fullyAlignedFaces.Add(item.Key);
            }
            else if (tryNonPerpendicularExtrusion && item.Value == ExtrusionAnalyzerFaceAlignment.Unaligned)
               candidateEndFaces.Add(item.Key);
         }

         if (candidateEndFaces.Count != 2)
            return false;

         if (checkOrdinarity && (allFaces.Count - fullyAlignedFaces.Count - candidateEndFaces.Count > 0))
            return false;

         if (!MathUtil.IsAlmostEqual(candidateEndFaces[0].Area, candidateEndFaces[1].Area))
            return false;

         // All faces will be planar at this time
         XYZ f1Normal = candidateEndFaces[0].ComputeNormal(new UV(0, 0));
         XYZ f2Normal = candidateEndFaces[1].ComputeNormal(new UV(0, 0));
         if (!f1Normal.IsAlmostEqualTo(f2Normal) && !f1Normal.IsAlmostEqualTo(f2Normal.Negate()))
            return false;

         HashSet<Face> adjoiningFaces = new HashSet<Face>();
         EdgeArray faceOuterBoundary = candidateEndFaces[0].EdgeLoops.get_Item(0);
         double f1Perimeter = 0;
         foreach (Edge edge in faceOuterBoundary)
         {
            Face adjoiningFace = edge.GetFace(1);
            if (adjoiningFace.Equals(candidateEndFaces[0]))
               adjoiningFace = edge.GetFace(0);
            adjoiningFaces.Add(adjoiningFace);
            f1Perimeter += edge.AsCurve().Length;
         }

         faceOuterBoundary = candidateEndFaces[1].EdgeLoops.get_Item(0);
         double f2Perimeter = 0;
         foreach (Edge edge in faceOuterBoundary)
         {
            Face adjoiningFace = edge.GetFace(1);
            if (adjoiningFace.Equals(candidateEndFaces[0]))
               adjoiningFace = edge.GetFace(0);
            if (adjoiningFaces.Contains(adjoiningFace))
               adjoiningFaces.Remove(adjoiningFace);
            f2Perimeter += edge.AsCurve().Length;
         }

         if (!MathUtil.IsAlmostEqual(f1Perimeter, f2Perimeter) && adjoiningFaces.Count > 0)
            return false;

         IList<FaceBoundaryType> faceBoundaryTypes;
         faceBoundaries = GetFaceBoundaries(candidateEndFaces[0], XYZ.Zero, out faceBoundaryTypes);
         if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView && faceBoundaryTypes.Contains(FaceBoundaryType.Complex))
            return false;

         extrusionEndFaces.Add(candidateEndFaces[0]);
         extrusionEndFaces.Add(candidateEndFaces[1]);

         return true;
      }

      /// <summary>
      /// Function to get ExtrusionBase profile. It is used especially in IFC4 Reference View because of limitation of geometry in RV. When a structural member
      /// object is exported as tessellated geometry, profile information is still needed for a valid IfcMaterialProfile. This function does it 
      /// </summary>
      /// <param name="exporterIFC">exporterIFC</param>
      /// <param name="solid">the solid geometry</param>
      /// <param name="profileName">profile name</param>
      /// <param name="basePlane">base plane of the profile</param>
      /// <param name="planeOrigin">the plane origin</param>
      /// <param name="extrusionEndFaces">output extrusion end faces</param>
      /// <param name="tryNonPerpendicularExtrusion">option to try non perpendicular extrusion</param>
      /// <returns>returns the handle to the profile</returns>
      public static IFCAnyHandle GetExtrusionBaseProfile(ExporterIFC exporterIFC, Solid solid, string profileName, Plane basePlane, XYZ planeOrigin,
         out IList<Face> extrusionEndFaces, bool tryNonPerpendicularExtrusion = false)
      {
         IFCAnyHandle extrudedAreaProfile = null;
         IList<CurveLoop> faceBoundaries = null;

         if (!TryGetExtrusionEndFaces(solid, basePlane, planeOrigin, tryNonPerpendicularExtrusion,
            checkOrdinarity: false, out extrusionEndFaces, out faceBoundaries))
            return extrudedAreaProfile;

         try
         {
            // For IFC4 RV, only IfcIndexedPolyCurve can be created, use CreateIFCCurveFromCurveLoop to create the IFC curve and use the default/identity transform for it
            IFCAnyHandle curveHandle = GeometryUtil.CreateCompositeOrIndexedCurve(exporterIFC, faceBoundaries[0].ToList(), Transform.Identity, faceBoundaries[0].GetPlane().Normal);
            if (faceBoundaries.Count == 1)
            {
               extrudedAreaProfile = IFCInstanceExporter.CreateArbitraryClosedProfileDef(exporterIFC.GetFile(), IFCProfileType.Curve, profileName, curveHandle);
            }
            else
            {
               HashSet<IFCAnyHandle> innerCurves = new HashSet<IFCAnyHandle>();
               for (int ii = 1; ii < faceBoundaries.Count; ++ii)
               {
                  IFCAnyHandle innerCurveHandle = GeometryUtil.CreateCompositeOrIndexedCurve(exporterIFC, faceBoundaries[ii].ToList(), Transform.Identity, faceBoundaries[ii].GetPlane().Normal);
                  innerCurves.Add(innerCurveHandle);
               }
               extrudedAreaProfile = IFCInstanceExporter.CreateArbitraryProfileDefWithVoids(exporterIFC.GetFile(), IFCProfileType.Area, profileName, curveHandle,
                  innerCurves);
            }

            return extrudedAreaProfile;
         }
         catch
         {
            return null;
         }
      }

      /// <summary>
      /// Attempt to get profile and simple material information from a FamilyInstance element.
      /// </summary>
      /// <param name="exporterIFC">The exporterIFC that contains state information for the export.</param>
      /// <param name="element">The element, expected to be a FamilyInstance.</param>
      /// <param name="basePlaneNormal">The normal used to try to find the profile.</param>
      /// <param name="basePlaneOrigin">The original for the profile.</param>
      /// <returns></returns>
      public static MaterialAndProfile GetProfileAndMaterial(ExporterIFC exporterIFC,
         Element element, XYZ basePlaneNormal, XYZ basePlaneOrigin)
      {
         MaterialAndProfile materialAndProfile = new MaterialAndProfile();

         FamilyInstance familyInstance = element as FamilyInstance;
         if (familyInstance == null)
            return null;

         FamilySymbol originalFamilySymbol = ExporterIFCUtils.GetOriginalSymbol(familyInstance);
         string profileName = NamingUtil.GetProfileName(originalFamilySymbol);

         // Check for swept profile
         try
         {
            SweptProfile sweptProfileFromFamInst = familyInstance.GetSweptProfile();

            IList<Curve> profileCurves = new List<Curve>();
            foreach (Curve curv in sweptProfileFromFamInst.GetSweptProfile().Curves)
               profileCurves.Add(curv);

            // What if there are multiple materials or multiple profiles in the family??
            XYZ projDir = XYZ.BasisZ;
            try
            {
               CurveLoop curveloop = CurveLoop.Create(profileCurves);
               if (curveloop.HasPlane())
                  projDir = curveloop.GetPlane().Normal;
            }
            catch
            {
               projDir = null;
            }

            IFCAnyHandle compCurveHandle = GeometryUtil.CreateCompositeOrIndexedCurve(exporterIFC, profileCurves, Transform.Identity, projDir);

            IFCAnyHandle profileDef = IFCInstanceExporter.CreateArbitraryClosedProfileDef(exporterIFC.GetFile(), IFCProfileType.Curve, profileName, compCurveHandle);

            ElementId materialId = familyInstance.GetMaterialIds(false).FirstOrDefault();
            if (materialId == null)
               materialId = ElementId.InvalidElementId;
            materialAndProfile.Add(materialId, profileDef);
         }
         catch
         {
            // Do nothing, will go to the next step
         }

         // If no Sweep, handle for Extrusion using the OriginalSymbol
         // TODO: We probably shouldn't be re-getting this information - it is already probably
         // already at the caller level.  Also, if this is a view specific export, then
         // the material information may be incorrect, since the view may have overriden
         // materials.
         {
            Plane basePlane = GeometryUtil.CreatePlaneByNormalAtOrigin(basePlaneNormal);
            Element exportGeometryElement = (Element)originalFamilySymbol;
            GeometryElement exportGeometry = exportGeometryElement.get_Geometry(new Options());
            foreach (GeometryObject geomObject in exportGeometry)
            {
               if (geomObject is Solid)
               {
                  IList<Face> extrusionEndFaces;
                  Solid solid = geomObject as Solid;
                  if (solid == null || solid.Faces.IsEmpty || solid.Edges.IsEmpty)
                     continue;

                  ElementId materialId = BodyExporter.GetBestMaterialIdFromGeometryOrParameter(solid, familyInstance);
                  IFCAnyHandle profileDef = GetExtrusionBaseProfile(exporterIFC, solid, profileName, basePlane, basePlaneOrigin, out extrusionEndFaces);
                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(profileDef))
                     materialAndProfile.Add(materialId, profileDef);
               }
            }
         }

         return materialAndProfile;
      }

      /// <summary>
      /// Intersect a plane with a line and return the intersecting point if any
      /// </summary>
      /// <param name="plane">the plane</param>
      /// <param name="line">the line</param>
      /// <param name="intersectingPoint">the intersecting point</param>
      /// <returns>true if the intersection is found</returns>
      public static bool PlaneIntersect(Plane plane, Line line, out XYZ intersectingPoint)
      {
         bool isBound = line.IsBound;
         XYZ refPointLine;
         if (isBound)
            refPointLine = line.GetEndPoint(0);    // use the line staring point
         else
            refPointLine = line.Origin;

         intersectingPoint = XYZ.Zero;
         double denom = plane.Normal.DotProduct(line.Direction);
         if (MathUtil.IsAlmostZero(denom))
            return false;   // Normal and the lines are perpendicular to each other: line is parallel to the plane, no intersection

         double rr = plane.Normal.DotProduct(new XYZ(plane.Origin.X - refPointLine.X, plane.Origin.Y - refPointLine.Y, plane.Origin.Z - refPointLine.Z))
                     / denom;

         if (isBound && (rr < 0.0 || rr > 1.0))
            return false;        // intersection occurs outside of the bound

         intersectingPoint = new XYZ(refPointLine.X + rr * line.Direction.X, refPointLine.Y + rr * line.Direction.Y, refPointLine.Z + rr * line.Direction.Z);
         return true;
      }

      public static bool PlaneIntersect(Plane P1, Plane P2, out Line intersectingLine)
      {
         intersectingLine = null;
         if (P1.Normal.IsAlmostEqualTo(P2.Normal))
            return false;                 // THe planes are parallel to each other, no intersection

         // Find intersecting line using 3 plane intersection
         XYZ intLineDir = P1.Normal.CrossProduct(P2.Normal);
         double da = 1 / (P1.Normal.DotProduct(P2.Normal.CrossProduct(intLineDir)));
         double P1parConstant = -(P1.Normal.X * P1.Origin.X + P1.Normal.Y * P1.Origin.Y + P1.Normal.Z * P1.Origin.Z);
         double P2parConstant = -(P2.Normal.X * P2.Origin.X + P2.Normal.Y * P2.Origin.Y + P2.Normal.Z * P2.Origin.Z);

         XYZ intLinePoint = (-P1parConstant * (P2.Normal.CrossProduct(intLineDir)) - P2parConstant * (intLineDir.CrossProduct(P1.Normal))) * da;
         Line intLine = Line.CreateUnbound(intLinePoint, intLineDir);
         return true;
      }

      /// <summary>
      /// Intersect a plane and the arc
      /// </summary>
      /// <param name="plane"></param>
      /// <param name="arc"></param>
      /// <param name="intersectingPoints"></param>
      /// <returns></returns>
      public static bool PlaneIntersect(Plane plane, Curve curve, out IList<XYZ> intersectingPoints)
      {
         intersectingPoints = new List<XYZ>();

         if (curve is CylindricalHelix)
            return false;              // Supports only a planar based curve

         // Get the plane where the curve lies
         Plane planeOfCurve = PlanarPlaneOf(curve);
         if (plane.Normal.IsAlmostEqualTo(planeOfCurve.Normal))
            return false;                 // THe planes are parallel to each other, no intersection

         // Find intersecting line 
         Line intLine = null;
         if (!PlaneIntersect(plane, planeOfCurve, out intLine))
            return false;

         // Intersect the curve with the plane intersection line
         try
         {
            CurveIntersectResult result = curve.Intersect(intLine, CurveIntersectResultOption.Detailed);
            if (null == result || result.Result == SetComparisonResult.Disjoint)
            {
               return false;
            }

            foreach (CurveOverlapPoint point in result.GetOverlaps().Where(e => CurveOverlapPointType.Intersection == e?.Type))
            {
               intersectingPoints.Add(point.Point);
            }
         }
         catch
         {
            return false;
         }
         return true;
      }

      public static Plane PlanarPlaneOf(Curve curve)
      {
         // We will only try up to six different parameters to find the 3rd point on the curve.
         IList<double> parToTry = new List<double>() { 0.15, 0.3, 0.45, 0.6, 0.75, 0.9 };

         // Get end points
         XYZ P1 = curve.GetEndPoint(0);
         XYZ P2 = curve.GetEndPoint(1);
         XYZ vec1 = new XYZ((P2.X - P1.X), (P2.Y - P1.Y), (P2.Z - P1.Z)).Normalize();

         // Get the third point on the curve by getting a point in the middle of the curve using parameter
         XYZ P3 = null;
         foreach (double par in parToTry)
         {
            P3 = curve.Evaluate(par, false);
            XYZ vec2 = new XYZ((P3.X - P1.X), (P3.Y - P1.Y), (P3.Z - P1.Z)).Normalize();
            // if the result is already a point not on the the same line, exit
            if (!vec1.IsAlmostEqualTo(vec2))
               break;
         }

         double d1X = P2.X - P1.X;
         double d1Y = P2.Y - P1.Y;
         double d1Z = P2.Z - P1.Z;
         XYZ v1 = new XYZ(d1X, d1Y, d1Z);

         double d2X = P3.X - P1.X;
         double d2Y = P3.Y - P1.Y;
         double d2Z = P3.Z - P1.Z;
         XYZ v2 = new XYZ(d2X, d2Y, d2Z);

         XYZ normal = v1.CrossProduct(v2);
         normal.Normalize();
         Plane planeOfArc = Plane.CreateByNormalAndOrigin(normal, P1);
         return planeOfArc;
      }

      /// <summary>
      /// Function to collect Family geometry element data summary for comparison purpose
      /// </summary>
      /// <param name="geomElement">the family geometry element</param>
      /// <returns>FamyGeometrySummaryData</returns>
      public static FamilyGeometrySummaryData CollectFamilyGeometrySummaryData(GeometryElement geomElement)
      {
         FamilyGeometrySummaryData famGeomData = new FamilyGeometrySummaryData();
         foreach (GeometryObject geomObj in geomElement)
         {
            if (geomObj is Curve)
            {
               famGeomData.CurveCount++;
               famGeomData.CurveLengthTotal += (geomObj as Curve).Length;
            }
            else if (geomObj is Edge)
            {
               famGeomData.EdgeCount++;
            }
            else if (geomObj is Face)
            {
               famGeomData.FaceCount++;
               famGeomData.FaceAreaTotal += (geomObj as Face).Area;
            }
            else if (geomObj is GeometryInstance)
            {
               famGeomData.GeometryInstanceCount++;
            }
            else if (geomObj is GeometryElement)
            {
               famGeomData.Add(CollectFamilyGeometrySummaryData(geomObj as GeometryElement));
            }
            else if (geomObj is Mesh)
            {
               famGeomData.MeshCount++;
               famGeomData.MeshNumberOfTriangleTotal += (geomObj as Mesh).NumTriangles;
            }
            else if (geomObj is Point)
            {
               famGeomData.PointCount++;
            }
            else if (geomObj is PolyLine)
            {
               famGeomData.PolylineCount++;
               famGeomData.PolylineNumberOfCoordinatesTotal += (geomObj as PolyLine).NumberOfCoordinates;
            }
            else if (geomObj is Profile)
            {
               famGeomData.ProfileCount++;
            }
            else if (geomObj is Solid)
            {
               famGeomData.SolidCount++;
               famGeomData.SolidVolumeTotal += (geomObj as Solid).Volume;
               famGeomData.SolidSurfaceAreaTotal += (geomObj as Solid).SurfaceArea;
               famGeomData.SolidFacesCountTotal += (geomObj as Solid).Faces.Size;
               famGeomData.SolidEdgesCountTotal += (geomObj as Solid).Edges.Size;
            }
         }
         return famGeomData;
      }

      /// <summary>
      /// Evaluate whether we should use the geomtry from the family instance, or we can use the common one from the Symbol
      /// </summary>
      /// <param name="familyInstance">the family instance</param>
      /// <returns>true/false</returns>
      public static bool UsesInstanceGeometry(FamilyInstance familyInstance)
      {
         GeometryElement famInstGeom = familyInstance.get_Geometry(GetIFCExportGeometryOptions());
         FamilyGeometrySummaryData instData = CollectFamilyGeometrySummaryData(famInstGeom);
         if (instData.OnlyContainsGeometryInstance())
            return false;

         GeometryElement famSymbolGeom = familyInstance.Symbol.get_Geometry(GetIFCExportGeometryOptions());
         FamilyGeometrySummaryData symbolData = CollectFamilyGeometrySummaryData(famSymbolGeom);
         if (instData.Equal(symbolData))
            return false;

         return true;
      }

      /// <summary>
      /// Get Arc or Line from Family Symbol given a family instance. This works by finding the related 2D geometries that can be obtained from the Plan View
      /// </summary>
      /// <param name="element">the family instance</param>
      /// <param name="allCurveType">set it to true if all 2D based curves are to be included</param>
      /// <param name="inclArc">set it to true if only the Arc to be included</param>
      /// <param name="inclLine">set it to true if only Line to be included</param>
      /// <param name="inclEllipse">set it to true if only Ellipse to be included</param>
      /// <param name="inclSpline">set it to true if only Splines to be included (incl. HermitSpline and NurbSpline)</param>
      /// <returns>the List of 2D curves found</returns>
      public static IList<Curve> Get2DArcOrLineFromSymbol(FamilyInstance element, bool allCurveType,
         bool inclArc = false, bool inclLine = false, bool inclEllipse = false, bool inclSpline = false)
      {
         IList<Curve> curveList = new List<Curve>();
         // If all curve option is set, set all flags to true
         if (!allCurveType && !(inclArc || inclLine || inclEllipse || inclSpline)) if (allCurveType)
               return curveList;       // Nothing is marked included, return empty list

         IList<Curve> curveListCache;
         if (!ExporterCacheManager.Object2DCurvesCache.TryGetValue(element.Symbol.Id, out curveListCache))
         {
            Document doc = element.Document;
            if (element.LevelId == ElementId.InvalidElementId)
               return curveList;

            Level level = element.Document.GetElement(element.LevelId) as Level;
            if (level.FindAssociatedPlanViewId() == ElementId.InvalidElementId)
               return curveList;

            ViewPlan planView = doc.GetElement(level.FindAssociatedPlanViewId()) as ViewPlan;

            Options defaultOptions = GetIFCExportGeometryOptions();
            Options currentOptions = new Options();
            currentOptions.View = planView;
            currentOptions.ComputeReferences = defaultOptions.ComputeReferences;
            currentOptions.IncludeNonVisibleObjects = defaultOptions.IncludeNonVisibleObjects;

            curveListCache = new List<Curve>();

            GeometryElement geoms = element.Symbol.get_Geometry(currentOptions);
            foreach (GeometryObject geomObj in geoms)
            {
               if (geomObj is Curve)
                  curveListCache.Add(geomObj as Curve);
            }

            // Add into the cache to reduce repeated efforts to get the 2D geometries for the same Symbol
            ExporterCacheManager.Object2DCurvesCache.Add(element.Symbol.Id, curveListCache);
         }

         if (allCurveType)
         {
            return curveListCache;
         }

         foreach (Curve curve in curveListCache)
         {
            if (inclArc && curve is Arc)
               curveList.Add(curve);
            else if (inclLine && curve is Line)
               curveList.Add(curve);
            else if (inclEllipse && curve is Ellipse)
               curveList.Add(curve);
            else if (inclSpline && (curve is HermiteSpline || curve is NurbSpline))
               curveList.Add(curve);
         }

         return curveList;
      }

      enum NormalDirection
      {
         UsePosX,
         UsePosY,
         UsePosZ,
         UseNegX,
         UseNegY,
         UseNegZ
      }

      private static bool UseThisDirection(double dirComp, double normComp, double eps)
      {
         return (MathUtil.IsAlmostEqual(dirComp, 1.0) && normComp > eps) ||
            (MathUtil.IsAlmostEqual(dirComp, -1.0) && normComp < eps);
      }

      /// <summary>
      /// Get the largest face from a Solid
      /// </summary>
      /// <param name="geomObj">the geometry Solid</param>
      /// <param name="normalDirection">Normal direction of the face to return (only accept (1,0,0), (0,1,0), or (0,0,1))</param>
      /// <returns>the largest face</returns>
      public static Face GetLargestFaceInSolid(GeometryObject geomObj, XYZ normalDirection)
      {
         Face largestFace = null;
         double largestArea = 0.0;

         if (geomObj == null)
            return largestFace;

         Solid geomSolid = geomObj as Solid;
         if (geomSolid == null)
            return largestFace;

         double eps = MathUtil.Eps();
         foreach (Face face in geomSolid.Faces)
         {
            // Identifying the largest area with normal pointing up
            XYZ faceNormal = face.ComputeNormal(new UV());

            if (!UseThisDirection(normalDirection.X, faceNormal.X, eps) &&
               !UseThisDirection(normalDirection.Y, faceNormal.Y, eps) &&
               !UseThisDirection(normalDirection.Z, faceNormal.Z, eps))
               continue;

            if (face.Area > largestArea)
            {
               largestFace = face;
               largestArea = face.Area;
            }
         }

         return largestFace;
      }

      /// <summary>
      /// Get face angle/slope. This will be calculated at UV (0,0)
      /// </summary>
      /// <param name="face">the face</param>
      /// <param name="projection">the XYZ to which the </param>
      /// <returns>the slope angle</returns>
      public static double GetAngleOfFace(Face face, XYZ projection)
      {
         double angle = 0.0;

         // Compute normal at UV (0,0)
         XYZ faceNormal = face.ComputeNormal(new UV());
         projection = projection.Normalize();
         double normalAngleToProjection = MathUtil.SafeAcos(faceNormal.DotProduct(projection));
         double slopeAngle = 0.5 * Math.PI - normalAngleToProjection;
         angle = UnitUtil.ScaleAngle(slopeAngle);

         return angle;
      }

      /// <summary>
      /// Iterate and recurse GeometryElement to find all Curves
      /// </summary>
      /// <param name="geomElem">the GeometryElement</param>
      /// <returns>List of Curves found in the GeometryElement</returns>
      public static List<Curve> GetCurvesFromGeometryElement(GeometryElement geomElem)
      {
         List<Curve> curveList = new List<Curve>();
         foreach (GeometryObject geomObject in geomElem)
         {
            if (geomObject is GeometryElement)
            {
               curveList.AddRange(GetCurvesFromGeometryElement(geomObject as GeometryElement));
            }
            else if (geomObject is GeometryInstance)
            {
               GeometryElement instGeom = (geomObject as GeometryInstance).GetInstanceGeometry();
               if (instGeom != null)
                  curveList.AddRange(GetCurvesFromGeometryElement(instGeom));
            }
            else if (geomObject is Curve)
            {
               curveList.Add(geomObject as Curve);
            }
         }

         return curveList;
      }

      /// <summary>
      /// Compare 2 solids using the geometry signature taken from: Boundingbox (Min and Max coordinates), SurfaceArea, Volume, No. of Faces, No. of Edges
      /// BEWARE that this is a quick compare and does not guarantee 100% equality (for example solid being mirrored will give false equality), but this is a very quick
      /// compare function and will work well if we really need to compare solids coming from the same source geometry.
      /// </summary>
      /// <param name="solid1">solid 1</param>
      /// <param name="solid2">solid 2</param>
      /// <returns>whether the 2 solids are equal based on their signature only</returns>
      public static bool SolidsQuickEqualityCompare(Solid solid1, Solid solid2)
      {
         // BEWARE that this is a quick compare and does not guarantee 100% equality (for example solid being mirrored will give false equality), but this is a very quick
         // compare function and will work well if we really need to compare solids coming from the same source geometry.
         if ((solid1.GetBoundingBox().Min.IsAlmostEqualTo(solid2.GetBoundingBox().Min))
            && (solid1.GetBoundingBox().Max.IsAlmostEqualTo(solid2.GetBoundingBox().Max))
            && MathUtil.IsAlmostEqual(solid1.SurfaceArea, solid2.SurfaceArea)
            && MathUtil.IsAlmostEqual(solid1.Volume, solid2.Volume)
            && (solid1.Faces.Size == solid2.Faces.Size)
            && (solid1.Edges.Size == solid2.Edges.Size))
            return true;
         else
            return false;
      }

      /// <summary>
      /// Get Site local placement depending on the coordinate reference selected (unscaled)
      /// </summary>
      /// <param name="doc">the Document</param>
      /// <returns>site local placement transform</returns>
      public static Transform GetSiteLocalPlacement(Document doc)
      {
         Transform trf = null;
         ProjectLocation projLocation = ExporterCacheManager.SelectedSiteProjectLocation;
         if (projLocation == null)
            return trf;

         using (SubTransaction projLocTr = new SubTransaction(doc))
         {
            projLocTr.Start();
            doc.ActiveProjectLocation = projLocation;

            BasePoint surveyPoint = BasePoint.GetSurveyPoint(doc);
            BasePoint projectBasePoint = BasePoint.GetProjectBasePoint(doc);
            if (surveyPoint == null || projectBasePoint == null)
               return trf;

            (double svNorthings, double svEastings, double svElevation, double svAngle, double pbNorthings,
               double pbEastings, double pbElevation, double pbAngle) = OptionsUtil.ProjectLocationInfo(doc, surveyPoint.Position, projectBasePoint.Position);

            SiteTransformBasis transformBasis = ExporterCacheManager.ExportOptionsCache.SiteTransformation;

            trf = Transform.Identity;
            Transform rotationTrfAtInternal = Transform.CreateRotationAtPoint(XYZ.BasisZ, pbAngle, XYZ.Zero);

            // For Linked file, the WCS should always be based on the shared coordinates
            if (ExporterUtil.ExportingHostModel())
            {
               switch (transformBasis)
               {
                  case SiteTransformBasis.Shared:
                     XYZ intPointOffset = rotationTrfAtInternal.OfPoint(projectBasePoint.Position);
                     XYZ xyz = new XYZ((projectBasePoint.SharedPosition.X - intPointOffset.X),
                                    (projectBasePoint.SharedPosition.Y - intPointOffset.Y),
                                     (projectBasePoint.SharedPosition.Z - intPointOffset.Z));
                     trf = CreateTransformFromVectorsAndOrigin(rotationTrfAtInternal.BasisX, rotationTrfAtInternal.BasisY, rotationTrfAtInternal.BasisZ, xyz);
                     break;
                  case SiteTransformBasis.Site:
                     xyz = rotationTrfAtInternal.OfPoint(surveyPoint.Position);
                     xyz = new XYZ(-xyz.X, -xyz.Y, -xyz.Z);
                     trf = CreateTransformFromVectorsAndOrigin(rotationTrfAtInternal.BasisX, rotationTrfAtInternal.BasisY, rotationTrfAtInternal.BasisZ, xyz);
                     break;
                  case SiteTransformBasis.Project:
                     xyz = projectBasePoint.Position;
                     xyz = new XYZ(-xyz.X, -xyz.Y, -xyz.Z);
                     trf = CreateTransformFromVectorsAndOrigin(Transform.Identity.BasisX, Transform.Identity.BasisY, Transform.Identity.BasisZ, xyz);
                     break;
                  case SiteTransformBasis.ProjectInTN:
                     xyz = rotationTrfAtInternal.OfPoint(projectBasePoint.Position);
                     xyz = new XYZ(-xyz.X, -xyz.Y, -xyz.Z);
                     trf = CreateTransformFromVectorsAndOrigin(rotationTrfAtInternal.BasisX, rotationTrfAtInternal.BasisY, rotationTrfAtInternal.BasisZ, xyz);
                     break;
                  case SiteTransformBasis.Internal:
                     xyz = new XYZ(0.0, 0.0, 0.0);
                     trf = CreateTransformFromVectorsAndOrigin(Transform.Identity.BasisX, Transform.Identity.BasisY, Transform.Identity.BasisZ, xyz);
                     break;
                  case SiteTransformBasis.InternalInTN:
                     xyz = rotationTrfAtInternal.OfPoint(new XYZ(0.0, 0.0, 0.0));
                     trf = CreateTransformFromVectorsAndOrigin(rotationTrfAtInternal.BasisX, rotationTrfAtInternal.BasisY, rotationTrfAtInternal.BasisZ, xyz);
                     break;
                  default:
                     break;
               }
            }
            projLocTr.RollBack();
         }
         return trf;
      }
   }
}
