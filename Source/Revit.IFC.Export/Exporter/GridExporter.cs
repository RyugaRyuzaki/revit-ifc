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
using Revit.IFC.Export.Utility;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Common.Utility;
using Revit.IFC.Common.Enums;

namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// Provides methods to export Grid.
   /// </summary>
   class GridExporter
   {
      class TupleGridAndNameComparer : IEqualityComparer<Tuple<ElementId, string>>
      {
         public bool Equals(Tuple<ElementId, string> tup1, Tuple<ElementId, string> tup2)
         {
            bool sameLevelId = tup1.Item1 == tup2.Item1;
            bool sameNameGroup = tup1.Item2.Equals(tup2.Item2, StringComparison.CurrentCultureIgnoreCase);
            if (sameLevelId && sameNameGroup)
               return true;
            else
               return false;
         }

         public int GetHashCode(Tuple<ElementId, string> tup)
         {
            int hashCode = tup.Item1.GetHashCode() ^ tup.Item2.GetHashCode();
            return hashCode;
         }
      }

      /// <summary>
      /// Export the Grids.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="document">The document object.</param>
      public static void Export(ExporterIFC exporterIFC, Document document)
      {
         if (ExporterCacheManager.GridCache.Count == 0)
            return;

         // Get all the grids from cache and sorted in levels.
         //IDictionary<ElementId, List<Grid>> levelGrids = GetAllGrids(exporterIFC);
         IDictionary<Tuple<ElementId, string>, List<Grid>> levelGrids = GetAllGrids(document);
         
         // Get grids in each level and export.
         foreach (Tuple<ElementId,string> levelId in levelGrids.Keys)
         {
            IDictionary<XYZ, List<Grid>> linearGrids = new SortedDictionary<XYZ, List<Grid>>(new GeometryUtil.XYZComparer());
            IDictionary<XYZ, List<Grid>> radialGrids = new SortedDictionary<XYZ, List<Grid>>(new GeometryUtil.XYZComparer());
            List<Grid> exportedLinearGrids = new List<Grid>();

            List<Grid> gridsOneLevel = levelGrids[levelId];
            string gridName = levelId.Item2;
            SortGrids(gridsOneLevel, out linearGrids, out radialGrids);

            // Export radial grids first.
            if (radialGrids.Count > 0)
            {
               ExportRadialGrids(exporterIFC, levelId.Item1, gridName, radialGrids, linearGrids);
            }

            // Export the rectangular and duplex rectangular grids.
            if (linearGrids.Count > 1)
            {
               ExportRectangularGrids(exporterIFC, levelId.Item1, gridName, linearGrids);
            }

            // Export the triangular grids
            if (linearGrids.Count > 1)
            {
               ExportTriangularGrids(exporterIFC, levelId.Item1, gridName, linearGrids);
            }

            // TODO: warn user about orphaned grid lines.
            if (linearGrids.Count == 1)
               continue;// not export the orphan grid (only has U).
         }
      }

      /// <summary>
      /// Export all the radial Grids.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="levelId">The level id.</param>
      /// <param name="radialGrids">The set of radial grids.</param>
      /// <param name="linearGrids">The set of linear grids.</param>
      public static void ExportRadialGrids(ExporterIFC exporterIFC, ElementId levelId, 
         string gridName, IDictionary<XYZ, List<Grid>> radialGrids, 
         IDictionary<XYZ, List<Grid>> linearGrids)
      {
         foreach (XYZ centerPoint in radialGrids.Keys)
         {
            List<Grid> exportedLinearGrids = new List<Grid>();
            List<Grid> radialUAxes = new List<Grid>();
            List<Grid> radialVAxes = new List<Grid>();
            radialUAxes = radialGrids[centerPoint];
            foreach (XYZ directionVector in linearGrids.Keys)
            {
               foreach (Grid linearGrid in linearGrids[directionVector])
               {
                  Line newLine = linearGrid.Curve.Clone() as Line;
                  newLine.MakeUnbound();
                  if (MathUtil.IsAlmostEqual(newLine.Project(centerPoint).Distance, 0.0))
                  {
                     radialVAxes.Add(linearGrid);
                  }
               }
            }

            // TODO: warn user about orphaned grid lines.
            if (radialVAxes.Count == 0)
               continue; //not export the orphan grid (only has U).

            // export a radial IFCGrid.
            string hashCode = centerPoint.ToString();
            ExportGrid(exporterIFC, levelId, gridName, hashCode, radialUAxes, radialVAxes, null);

            // remove the linear grids that have been exported.
            exportedLinearGrids = exportedLinearGrids.Union<Grid>(radialVAxes).ToList();
            RemoveExportedGrids(linearGrids, exportedLinearGrids);
         }
      }

      /// <summary>
      /// Export all the rectangular Grids.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="levelId">The level id.</param>
      /// <param name="linearGrids">The set of linear grids.</param>
      public static void ExportRectangularGrids(ExporterIFC exporterIFC, ElementId levelId, string gridName, IDictionary<XYZ, List<Grid>> linearGrids)
      {
         XYZ uDirection = null;
         XYZ vDirection = null;
         List<XYZ> directionList = linearGrids.Keys.ToList();

         do
         {
            // Special case: we don't want to orphan one set of directions.
            if (directionList.Count == 3)
               return;

            if (!FindOrthogonalDirectionPair(directionList, out uDirection, out vDirection))
               return;

            List<Grid> exportedLinearGrids = new List<Grid>();
            List<Grid> duplexAxesU = FindParallelGrids(linearGrids, uDirection);
            List<Grid> duplexAxesV = FindParallelGrids(linearGrids, vDirection);

            // export a rectangular IFCGrid.
            string hashCode = uDirection.ToString() + ":" + vDirection.ToString();
            ExportGrid(exporterIFC, levelId, gridName, hashCode, duplexAxesU, duplexAxesV, null);

            // remove the linear grids that have been exported.
            exportedLinearGrids = exportedLinearGrids.Union<Grid>(duplexAxesU).ToList();
            exportedLinearGrids = exportedLinearGrids.Union<Grid>(duplexAxesV).ToList();
            if (exportedLinearGrids.Count > 0)
            {
               RemoveExportedGrids(linearGrids, exportedLinearGrids);
            }

            directionList = linearGrids.Keys.ToList();
         } while (true);
      }

      /// <summary>
      /// Export all the triangular Grids.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="levelId">The level id.</param>
      /// <param name="linearGrids">The set of linear grids.</param>
      public static void ExportTriangularGrids(ExporterIFC exporterIFC, ElementId levelId, string gridName, IDictionary<XYZ, List<Grid>> linearGrids)
      {
         List<XYZ> directionList = linearGrids.Keys.ToList();
         for (int ii = 0; ii < directionList.Count; ii += 3)
         {
            List<Grid> sameDirectionAxesU = new List<Grid>();
            List<Grid> sameDirectionAxesV = new List<Grid>();
            List<Grid> sameDirectionAxesW = new List<Grid>();
            sameDirectionAxesU = linearGrids[directionList[ii]];
            string hashCode = directionList[ii].ToString();

            if (ii + 1 < directionList.Count)
            {
               sameDirectionAxesV = linearGrids[directionList[ii + 1]];
               hashCode += ":" + directionList[ii + 1].ToString();
            }
            if (ii + 2 < directionList.Count)
            {
               sameDirectionAxesW = linearGrids[directionList[ii + 2]];
               hashCode += ":" + directionList[ii + 2].ToString();
            }

            // TODO: warn user about orphaned grid lines.
            if (sameDirectionAxesV.Count == 0)
               continue;//not export the orphan grid (only has U).

            // export a triangular IFCGrid.
            ExportGrid(exporterIFC, levelId, gridName, hashCode,
               sameDirectionAxesU, sameDirectionAxesV, sameDirectionAxesW);
         }
      }

      /// <summary>
      /// Export one IFCGrid in one level.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="levelId">The level ID.</param>
      /// <param name="gridName">The grid name.</param>
      /// <param name="hashCode">An extra code to generate a unique guid.</param>
      /// <param name="sameDirectionAxesU">The U axes of grids.</param>
      /// <param name="sameDirectionAxesV">The V axes of grids.</param>
      /// <param name="sameDirectionAxesW">The W axes of grids.</param>
      public static void ExportGrid(ExporterIFC exporterIFC, ElementId levelId, 
         string gridName, string hashCode,
         List<Grid> sameDirectionAxesU, List<Grid> sameDirectionAxesV, List<Grid> sameDirectionAxesW)
      {
         List<IFCAnyHandle> axesU = null;
         List<IFCAnyHandle> axesV = null;
         List<IFCAnyHandle> axesW = null;
         List<IFCAnyHandle> representations = new List<IFCAnyHandle>();

         using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
         {
            IFCFile ifcFile = exporterIFC.GetFile();
            using (IFCTransaction transaction = new IFCTransaction(ifcFile))
            {
               GridRepresentationData gridRepresentationData = new GridRepresentationData();

               axesU = CreateIFCGridAxisAndRepresentations(exporterIFC, productWrapper, 
                  sameDirectionAxesU, representations, gridRepresentationData);
               axesV = CreateIFCGridAxisAndRepresentations(exporterIFC, productWrapper, 
                  sameDirectionAxesV, representations, gridRepresentationData);
               if (sameDirectionAxesW != null)
                  axesW = CreateIFCGridAxisAndRepresentations(exporterIFC, productWrapper, 
                     sameDirectionAxesW, representations, gridRepresentationData);

               IFCRepresentationIdentifier identifier = IFCRepresentationIdentifier.FootPrint;
               string identifierOpt = identifier.ToString();
               IFCAnyHandle contextOfItemsFootPrint = ExporterCacheManager.Get3DContextHandle(identifier);
               string representationTypeOpt = "GeometricCurveSet";

               int numGridsToExport = gridRepresentationData.m_Grids.Count;
               if (numGridsToExport == 0)
                  return;

               bool useIFCCADLayer = !string.IsNullOrWhiteSpace(gridRepresentationData.m_IFCCADLayer);

               IFCAnyHandle shapeRepresentation = null;

               HashSet<IFCAnyHandle> allCurves = new HashSet<IFCAnyHandle>();
               for (int ii = 0; ii < numGridsToExport; ii++)
                  allCurves.UnionWith(gridRepresentationData.m_curveSets[ii]);

               if (useIFCCADLayer)
               {
                  shapeRepresentation = RepresentationUtil.CreateShapeRepresentation(exporterIFC, contextOfItemsFootPrint,
                     identifierOpt, representationTypeOpt, allCurves, gridRepresentationData.m_IFCCADLayer);
               }
               else
               {
                  ElementId catId = CategoryUtil.GetSafeCategoryId(gridRepresentationData.m_Grids[0]);
                  shapeRepresentation = RepresentationUtil.CreateShapeRepresentation(exporterIFC, gridRepresentationData.m_Grids[0], catId,
                     contextOfItemsFootPrint, identifierOpt, representationTypeOpt, allCurves);
               }
               representations.Add(shapeRepresentation);

               IFCAnyHandle productRep = IFCInstanceExporter.CreateProductDefinitionShape(ifcFile, null, null, representations);

               // We will associate the grid with its level, unless there are no levels in the file, in which case we'll associate it with the building.
               IFCLevelInfo levelInfo = ExporterCacheManager.LevelInfoCache.GetLevelInfo(levelId);
               bool useLevelInfo = (levelInfo != null);
               IFCAnyHandle gridLevelHandle = useLevelInfo ? levelInfo.GetBuildingStorey() : ExporterCacheManager.BuildingHandle;

               string gridGUID = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcGrid, gridName + ":" + hashCode, gridLevelHandle));

               IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;
               IFCAnyHandle levelObjectPlacement = (gridLevelHandle != null) ? IFCAnyHandleUtil.GetObjectPlacement(gridLevelHandle) : null;
               IFCAnyHandle copyLevelPlacement = (levelObjectPlacement != null) ? ExporterUtil.CopyLocalPlacement(ifcFile, levelObjectPlacement) : null;
               IFCAnyHandle ifcGrid = IFCInstanceExporter.CreateGrid(exporterIFC, gridGUID, 
                  ownerHistory, gridName, copyLevelPlacement, productRep, axesU, axesV, axesW);

               productWrapper.AddElement(null, ifcGrid, levelInfo, null, true, null);

               transaction.Commit();
            }
         }
      }

      public class GridRepresentationData
      {
         // The CAD Layer override.
         public string m_IFCCADLayer = null;

         // The ElementIds of the grids to export.
         public List<Element> m_Grids = new List<Element>();

         // The curve sets to export.
         public List<HashSet<IFCAnyHandle>> m_curveSets = new List<HashSet<IFCAnyHandle>>();
      }

      /// <summary>
      /// Get the handles of Grid Axes.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="sameDirectionAxes">The grid axes in the same direction of one level.</param>
      /// <param name="representations">The representation of grid axis.</param>
      /// <returns>The list of handles of grid axes.</returns>
      private static List<IFCAnyHandle> CreateIFCGridAxisAndRepresentations(ExporterIFC exporterIFC, ProductWrapper productWrapper, IList<Grid> sameDirectionAxes,
          IList<IFCAnyHandle> representations, GridRepresentationData gridRepresentationData)
      {
         if (sameDirectionAxes.Count == 0)
            return null;

         IDictionary<ElementId, List<IFCAnyHandle>> gridAxisMap = new Dictionary<ElementId, List<IFCAnyHandle>>();
         IDictionary<ElementId, List<IFCAnyHandle>> gridRepMap = new Dictionary<ElementId, List<IFCAnyHandle>>();

         IFCFile ifcFile = exporterIFC.GetFile();
         Line baseGridAxisAsLine = sameDirectionAxes[0].Curve as Line;

         Transform lcs = Transform.Identity;

         List<IFCAnyHandle> ifcGridAxes = new();
         XYZ projectionDirection = lcs.BasisZ;

         foreach (Grid grid in sameDirectionAxes)
         {
            // Because the IfcGrid is a collection of Revit Grids, any one of them can override the IFC CAD Layer.
            // We will take the first name, and not do too much checking.
            if (string.IsNullOrWhiteSpace(gridRepresentationData.m_IFCCADLayer))
            {
               gridRepresentationData.m_IFCCADLayer = RepresentationUtil.GetPresentationLayerOverride(grid);
            }

            Curve currentGridAxis = grid.Curve;
            bool sameSense = true;
            if (baseGridAxisAsLine != null)
            {
               Line axisLine = currentGridAxis as Line;
               sameSense = axisLine?.Direction.IsAlmostEqualTo(baseGridAxisAsLine.Direction) ?? true;
               if (!sameSense)
               {
                  currentGridAxis = currentGridAxis.CreateReversed();
               }
            }

            // Get the handle of curve.
            IFCAnyHandle axisCurve;
            if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
            {
               axisCurve = GeometryUtil.CreatePolyCurveFromCurve(exporterIFC, currentGridAxis, lcs, projectionDirection);
            }
            else
            {
               axisCurve = GeometryUtil.CreateIFCCurveFromRevitCurve(exporterIFC.GetFile(), exporterIFC, currentGridAxis,
                  false, null, GeometryUtil.TrimCurvePreference.UsePolyLineOrTrim, null);
            }

            IFCAnyHandle ifcGridAxis = IFCInstanceExporter.CreateGridAxis(ifcFile, grid.Name, axisCurve, sameSense);
            ifcGridAxes.Add(ifcGridAxis);

            HashSet<IFCAnyHandle> AxisCurves = new() { axisCurve };

            IFCAnyHandle repItemHnd = IFCInstanceExporter.CreateGeometricCurveSet(ifcFile, AxisCurves);

            // get the weight and color from the GridType to create the curve style.
            GridType gridType = grid.Document.GetElement(grid.GetTypeId()) as GridType;

            IFCData curveWidth = null;
            if (ExporterCacheManager.ExportOptionsCache.ExportAnnotations)
            {
               int outWidth;
               double width =
                   (ParameterUtil.GetIntValueFromElement(gridType, BuiltInParameter.GRID_END_SEGMENT_WEIGHT, out outWidth) != null) ? outWidth : 1;
               curveWidth = IFCDataUtil.CreateAsPositiveLengthMeasure(width);
            }

            if (!ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
            {
               int outColor;
               int color =
                   (ParameterUtil.GetIntValueFromElement(gridType, BuiltInParameter.GRID_END_SEGMENT_COLOR, out outColor) != null) ? outColor : 0;
               double blueVal = 0.0;
               double greenVal = 0.0;
               double redVal = 0.0;
               GeometryUtil.GetRGBFromIntValue(color, out blueVal, out greenVal, out redVal);
               IFCAnyHandle colorHnd = IFCInstanceExporter.CreateColourRgb(ifcFile, null, redVal, greenVal, blueVal);

               BodyExporter.CreateCurveStyleForRepItem(exporterIFC, repItemHnd, curveWidth, colorHnd);
            }

            HashSet<IFCAnyHandle> curveSet = new HashSet<IFCAnyHandle>();
            curveSet.Add(repItemHnd);

            gridRepresentationData.m_Grids.Add(grid);
            gridRepresentationData.m_curveSets.Add(curveSet);
         }

         return ifcGridAxes;
      }

      /// <summary>
      /// Get all the grids and add to the map with its level.
      /// </summary>
      /// <param name="document">The current document.</param>
      /// <returns>The map with sorted grids by level.</returns>
      private static IDictionary<Tuple<ElementId, string>, List<Grid>> GetAllGrids(Document document)
      {
         View currentView = ExporterCacheManager.ExportOptionsCache.FilterViewForExport;
         Level currentLevel = currentView?.GenLevel;
         
         SortedDictionary<double,ElementId> levelIds = new SortedDictionary<double,ElementId>();
         
         if (currentLevel != null)
         {
            levelIds.Add(currentLevel.ProjectElevation, currentLevel.Id);
         }
         else
         {
            foreach (ElementId levelId in ExporterCacheManager.LevelInfoCache.GetBuildingStoriesByElevation())
            {
               Level level = document.GetElement(levelId) as Level;
               double? projectElevation = level?.ProjectElevation;
               if (projectElevation.HasValue && !levelIds.ContainsKey(projectElevation.Value))
               {
                  levelIds.Add(projectElevation.Value, levelId);
               }
            }
         }

         double eps = MathUtil.Eps();
         // The Dictionary key is a tuple of the containing level id, and the elevation of the Grid
         IDictionary<Tuple<ElementId,string>, List<Grid>> levelGrids = new Dictionary<Tuple<ElementId, string>, List<Grid>>(new TupleGridAndNameComparer());

         // Group grids based on their elevation (the same elevation will be the same IfcGrid)
         foreach (Element element in ExporterCacheManager.GridCache)
         {
            Grid grid = element as Grid;
            XYZ minPoint = grid.GetExtents().MinimumPoint;
            XYZ maxPoint = grid.GetExtents().MaximumPoint;

            // Find level where the Grid min point is at higher elevation but lower than the next level
            KeyValuePair<double, ElementId> levelGrid = new KeyValuePair<double, ElementId>(0.0, ElementId.InvalidElementId);
            if (levelIds.Count != 0)
            {
               foreach (KeyValuePair<double, ElementId> levelInfo in levelIds)
               {
                  //if (levelInfo.Key + eps >= minPoint.Z)
                  //   break;
                  if (minPoint.Z <= levelInfo.Key + eps && levelInfo.Key - eps <= maxPoint.Z)
                  {
                     levelGrid = levelInfo;

                     string gridName = NamingUtil.GetNameOverride(element, "Default Grid");
                     Tuple<ElementId, string> gridGroupKey = new Tuple<ElementId, string>(levelGrid.Value, gridName);
                     if (!levelGrids.ContainsKey(gridGroupKey))
                        levelGrids.Add(gridGroupKey, new List<Grid>());
                     levelGrids[gridGroupKey].Add(grid);
                  }
               }
            } 
         }
         return levelGrids;
      }

      /// <summary>
      /// Sort the grids in linear and radial shape.
      /// </summary>
      /// <param name="gridsOneLevel">The grids in one level.</param>
      /// <param name="linearGrids">The linear grids in one level.</param>
      /// <param name="radialGrids">The radial grids in one level.</param>
      private static void SortGrids(List<Grid> gridsOneLevel, out IDictionary<XYZ, List<Grid>> linearGrids, out IDictionary<XYZ, List<Grid>> radialGrids)
      {
         linearGrids = new SortedDictionary<XYZ, List<Grid>>(new GeometryUtil.XYZComparer());
         radialGrids = new SortedDictionary<XYZ, List<Grid>>(new GeometryUtil.XYZComparer());

         foreach (Grid grid in gridsOneLevel)
         {
            Curve gridAxis = grid.Curve;
            if (gridAxis is Line)
            {
               Line line = gridAxis as Line;
               XYZ directionVector = line.Direction;
               if (!linearGrids.ContainsKey(directionVector))
               {
                  linearGrids.Add(directionVector, new List<Grid>());
               }

               linearGrids[directionVector].Add(grid);
            }
            else if (gridAxis is Arc)
            {
               Arc arc = gridAxis as Arc;
               XYZ arcCenter = arc.Center;
               if (!radialGrids.ContainsKey(arcCenter))
               {
                  radialGrids.Add(arcCenter, new List<Grid>());
               }

               radialGrids[arcCenter].Add(grid);
            }
         }
      }

      /// <summary>
      /// Remove the exported grids from set of linear grids.
      /// </summary>
      /// <param name="linearGrids">The set of linear grids.</param>
      /// <param name="exportedLinearGrids">The exported grids.</param>
      private static void RemoveExportedGrids(IDictionary<XYZ, List<Grid>> linearGrids, List<Grid> exportedLinearGrids)
      {
         foreach (Grid exportedGrid in exportedLinearGrids)
         {
            Line line = exportedGrid.Curve as Line;
            XYZ direction = line.Direction;
            if (linearGrids.ContainsKey(direction))
            {
               linearGrids[direction].Remove(exportedGrid);
               if (linearGrids[direction].Count == 0)
               {
                  linearGrids.Remove(direction);
               }
            }
         }
      }

      /// <summary>
      /// Find the orthogonal directions for rectangular IFCGrid.
      /// </summary>
      /// <param name="directionList">The directions.</param>
      /// <param name="uDirection">The U direction.</param>
      /// <param name="vDirection">The V direction.</param>
      /// <returns>True if find a pair of orthogonal directions for grids; false otherwise.</returns>
      private static bool FindOrthogonalDirectionPair(List<XYZ> directionList, out XYZ uDirection, out XYZ vDirection)
      {
         uDirection = null;
         vDirection = null;

         foreach (XYZ uDir in directionList)
         {
            foreach (XYZ vDir in directionList)
            {
               double dotProduct = uDir.DotProduct(vDir);
               if (MathUtil.IsAlmostEqual(Math.Abs(dotProduct), 0.0))
               {
                  uDirection = uDir;
                  vDirection = vDir;
                  return true;
               }
            }
         }
         return false;
      }

      /// <summary>
      /// Find the list of parallel linear grids via the given direction.
      /// </summary>
      /// <param name="linearGrids">The set of linear grids.</param>
      /// <param name="baseDirection">The given direction.</param>
      /// <returns>The list of parallel grids, containing the anti direction grids.</returns>
      private static List<Grid> FindParallelGrids(IDictionary<XYZ, List<Grid>> linearGrids, XYZ baseDirection)
      {
         List<XYZ> directionList = linearGrids.Keys.ToList();
         List<Grid> parallelGrids = linearGrids[baseDirection];
         foreach (XYZ direction in directionList)
         {
            if (baseDirection.IsAlmostEqualTo(direction))
               continue;
            double dotProduct = direction.DotProduct(baseDirection);
            if (MathUtil.IsAlmostEqual(dotProduct, -1.0))
            {
               parallelGrids = parallelGrids.Union(linearGrids[direction]).ToList();
               return parallelGrids;
            }
         }
         return parallelGrids;
      }
   }
}
