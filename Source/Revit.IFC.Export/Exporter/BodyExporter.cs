//
// BIM IFC library: this library works with Autodesk(R) Revit(R) to export IFC files containing model geometry.
// Copyright (C) 2012-2016  Autodesk, Inc.
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
using Revit.IFC.Export.Properties;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Export.Utility;
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;
using Autodesk.Revit.DB.Visual;

namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// Provides methods to export geometries to body representation.
   /// </summary>
   class BodyExporter
   {
      class FaceSetInfo
      {
         public FaceSetInfo(HashSet<IFCAnyHandle> faceSetHandles, bool isClosed)
         {
            FaceSetHandles = faceSetHandles;
            IsClosed = isClosed;
         }

         public HashSet<IFCAnyHandle> FaceSetHandles { get; } = new HashSet<IFCAnyHandle>(); 

         public bool IsClosed { get; } = false;
      }

      /// <summary>
      /// Sets best material id for current export state.
      /// </summary>
      /// <param name="geometryObject">The geometry object to get the best material id.</param>
      /// <param name="element">The element to get its structual material if no material found in its geometry.</param>
      /// <param name="overrideMaterialId">The material id to override the one gets from geometry object.</param>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <returns>The material id.</returns>
      public static ElementId SetBestMaterialIdInExporter(GeometryObject geometryObject, Element element, ElementId overrideMaterialId, ExporterIFC exporterIFC)
      {
         ElementId materialId = overrideMaterialId != ElementId.InvalidElementId ? overrideMaterialId :
             GetBestMaterialIdFromGeometryOrParameter(geometryObject, element);

         if (materialId != ElementId.InvalidElementId)
            exporterIFC.SetMaterialIdForCurrentExportState(materialId);

         return materialId;
      }

      /// <summary>
      /// Gets the best material id for the geometry.
      /// </summary>
      /// <remarks>
      /// The best material ID for a list of solid and meshes is not invalid if all solids and meshes with an ID have the same one.
      /// </remarks>
      /// <param name="solids">List of solids.</param>
      /// <param name="meshes">List of meshes.</param>
      /// <returns>The material id.</returns>
      public static ElementId GetBestMaterialIdForGeometry(IList<Solid> solids, IList<Mesh> meshes)
      {
         ElementId bestMaterialId = ElementId.InvalidElementId;
         int numSolids = solids.Count;
         int numMeshes = meshes.Count;
         if (numSolids + numMeshes == 0)
            return bestMaterialId;

         int currentMesh = 0;
         for (; (currentMesh < numMeshes); currentMesh++)
         {
            ElementId currentMaterialId = meshes[currentMesh].MaterialElementId;
            if (currentMaterialId != ElementId.InvalidElementId)
            {
               bestMaterialId = currentMaterialId;
               break;
            }
         }

         int currentSolid = 0;
         if (bestMaterialId == ElementId.InvalidElementId)
         {
            for (; (currentSolid < numSolids); currentSolid++)
            {
               if (solids[currentSolid].Faces.Size > 0)
               {
                  bestMaterialId = GetBestMaterialIdForGeometry(solids[currentSolid]);
                  break;
               }
            }
         }

         if (bestMaterialId != ElementId.InvalidElementId)
         {
            for (currentMesh++; (currentMesh < numMeshes); currentMesh++)
            {
               ElementId currentMaterialId = meshes[currentMesh].MaterialElementId;
               if (currentMaterialId != ElementId.InvalidElementId && currentMaterialId != bestMaterialId)
               {
                  bestMaterialId = ElementId.InvalidElementId;
                  break;
               }
            }
         }

         if (bestMaterialId != ElementId.InvalidElementId)
         {
            for (currentSolid++; (currentSolid < numSolids); currentSolid++)
            {
               if (solids[currentSolid].Faces.Size > 0)
                  continue;

               ElementId currentMaterialId = GetBestMaterialIdForGeometry(solids[currentSolid]);
               if (currentMaterialId != ElementId.InvalidElementId && currentMaterialId != bestMaterialId)
               {
                  bestMaterialId = ElementId.InvalidElementId;
                  break;
               }
            }
         }

         if (ExporterUtil.IsElementIdBuiltInOrInvalid(bestMaterialId))
            bestMaterialId = ElementId.InvalidElementId;

         return bestMaterialId;
      }

      /// <summary>
      /// Gets the best material id for the geometry.
      /// </summary>
      /// <param name="geometryElement">The geometry object to get the best material id.</param>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="range">The range to get the clipped geometry.</param>
      /// <returns>The material id.</returns>
      public static ElementId GetBestMaterialIdForGeometry(GeometryElement geometryElement,
         IFCRange range)
      {
         SolidMeshGeometryInfo solidMeshCapsule = (range == null) ?
            GeometryUtil.GetSolidMeshGeometry(geometryElement, Transform.Identity) :
            GeometryUtil.GetClippedSolidMeshGeometry(geometryElement, range);


         IList<Solid> solids = solidMeshCapsule.GetSolids();
         IList<Mesh> polyMeshes = solidMeshCapsule.GetMeshes();

         ElementId id = GetBestMaterialIdForGeometry(solids, polyMeshes);

         return id;
      }

      /// <summary>
      /// Gets the best material id for the geometry.
      /// </summary>
      /// <param name="geometryObject">The geometry object to get the best material id.</param>
      /// <returns>The material id.</returns>
      public static ElementId GetBestMaterialIdForGeometry(GeometryObject geometryObject)
      {
         if (geometryObject is GeometryElement)
            return GetBestMaterialIdForGeometry(geometryObject as GeometryElement, null);

         if (!(geometryObject is Solid))
         {
            ElementId matId = ElementId.InvalidElementId;
            if (geometryObject is Mesh)
            {
               matId = (geometryObject as Mesh).MaterialElementId;
            }
            else if (geometryObject is Face)
            {
               matId = (geometryObject as Face).MaterialElementId;
            }

            return ExporterUtil.IsElementIdBuiltInOrInvalid(matId) ? ElementId.InvalidElementId : 
               matId;
         }

         Solid solid = geometryObject as Solid;

         // We need to figure out the most common material id for the internal faces.
         // Other faces will override this.

         // We store the "most popular" material id.
         // We used to do this by counting how many faces had each id, and taking
         // the material that was used by the most faces.  However, this can result in
         // a material that is used on a very large face being overwhelmed by a material
         // that is used by a large number of small faces.  So we are replacing count
         // with total area.
         IDictionary<ElementId, double> countMap = new Dictionary<ElementId, double>();
         ElementId mostPopularId = ElementId.InvalidElementId;
         double mostPopularTotalArea = 0.0;

         foreach (Face face in solid.Faces)
         {
            if (face == null)
               continue;

            ElementId currentMaterialId = face.MaterialElementId;
            if (currentMaterialId == ElementId.InvalidElementId)
               continue;

            double currentTotalArea = 0.0;
            try
            {
               currentTotalArea = face.Area;
            }
            catch
            {
               continue;
            }

            if (countMap.ContainsKey(currentMaterialId))
            {
               countMap[currentMaterialId] += currentTotalArea;
               currentTotalArea = countMap[currentMaterialId];
            }
            else
            {
               countMap[currentMaterialId] = currentTotalArea;
            }

            // We add a small tolerance for stability, in cases where there
            // are two areas that are almost equal.  In this case, we use the smaller ElementId.
            if (MathUtil.IsAlmostEqual(currentTotalArea, mostPopularTotalArea))
            {
               mostPopularId = new ElementId(
                  Math.Min(currentMaterialId.Value, mostPopularId.Value));
            }
            else if (currentTotalArea > mostPopularTotalArea)
            {
               mostPopularId = currentMaterialId;
               mostPopularTotalArea = currentTotalArea;
            }
         }

         if (ExporterUtil.IsElementIdBuiltInOrInvalid(mostPopularId))
            mostPopularId = ElementId.InvalidElementId;

         return mostPopularId;
      }

      private static bool IsDuctCategory(ElementId categoryId)
      {
         long categoryValue = categoryId.Value;
         return categoryValue == (long)BuiltInCategory.OST_DuctAccessory ||
            categoryValue == (long)BuiltInCategory.OST_DuctCurves ||
            categoryValue == (long)BuiltInCategory.OST_DuctFitting ||
            categoryValue == (long)BuiltInCategory.OST_DuctInsulations ||
            categoryValue == (long)BuiltInCategory.OST_DuctLinings ||
            categoryValue == (long)BuiltInCategory.OST_DuctTerminal ||
            categoryValue == (long)BuiltInCategory.OST_FlexDuctCurves ||
            categoryValue == (long)BuiltInCategory.OST_PlaceHolderDucts;
      }

      private static bool IsPipeCategory(ElementId categoryId)
      {
         long categoryValue = categoryId.Value;
         return categoryValue == (long)BuiltInCategory.OST_AnalyticalPipeConnections ||
            categoryValue == (long)BuiltInCategory.OST_FlexPipeCurves ||
            categoryValue == (long)BuiltInCategory.OST_PipeCurves ||
            categoryValue == (long)BuiltInCategory.OST_PlaceHolderPipes ||
            categoryValue == (long)BuiltInCategory.OST_PlumbingFixtures ||
            categoryValue == (long)BuiltInCategory.OST_PipeAccessory ||
            categoryValue == (long)BuiltInCategory.OST_PipeFitting ||
            categoryValue == (long)BuiltInCategory.OST_PipeInsulations ||
            categoryValue == (long)BuiltInCategory.OST_Sprinklers;
      }

      private static bool CategoryHasMaterialIdParam(ElementId categoryId)
      {
         // OST_Cornices also has a MaterialId parameter, but Revit doesn't want us
         // to ask for it.
         long categoryValue = categoryId.Value;
         return categoryValue == (long)BuiltInCategory.OST_Rebar ||
            categoryValue == (long)BuiltInCategory.OST_FabricReinforcement ||
            categoryValue == (long)BuiltInCategory.OST_Fascia ||
            categoryValue == (long)BuiltInCategory.OST_Gutter ||
            categoryValue == (long)BuiltInCategory.OST_EdgeSlab ||
            categoryValue == (long)BuiltInCategory.OST_Parts ||
            categoryValue == (long)BuiltInCategory.OST_PipeInsulations ||
            categoryValue == (long)BuiltInCategory.OST_DuctInsulations ||
            categoryValue == (long)BuiltInCategory.OST_DuctLinings;
      }

      private static bool CategoryHasStructuralMaterialParam(ElementId categoryId)
      {
         long categoryValue = categoryId.Value;
         return categoryValue == (long)BuiltInCategory.OST_AnalyticalMember ||
            categoryValue == (long)BuiltInCategory.OST_AnalyticalPanel ||
            categoryValue == (long)BuiltInCategory.OST_BuildingPad ||
            categoryValue == (long)BuiltInCategory.OST_Ceilings ||
            categoryValue == (long)BuiltInCategory.OST_Floors ||
            categoryValue == (long)BuiltInCategory.OST_Roofs ||
            categoryValue == (long)BuiltInCategory.OST_StructConnectionAnchors ||
            categoryValue == (long)BuiltInCategory.OST_StructConnectionBolts ||
            categoryValue == (long)BuiltInCategory.OST_StructConnectionPlates ||
            categoryValue == (long)BuiltInCategory.OST_StructConnectionProfiles ||
            categoryValue == (long)BuiltInCategory.OST_StructConnectionShearStuds ||
            categoryValue == (long)BuiltInCategory.OST_StructuralColumns ||
            categoryValue == (long)BuiltInCategory.OST_StructuralFoundation ||
            categoryValue == (long)BuiltInCategory.OST_StructuralFraming ||
            categoryValue == (long)BuiltInCategory.OST_Walls;
      }

      private static ElementId GetBestMaterialIdFromParameter(Element element)
      {
         if (element == null)
         {
            return ElementId.InvalidElementId;
         }

         ElementId id = element.Id;
         if (ExporterCacheManager.ElementIdMaterialParameterCache.TryGetValue(id, out ElementId matId))
         {
            return matId;
         }

         matId = CalcBestMaterialIdFromParameter(element);
         ExporterCacheManager.ElementIdMaterialParameterCache[id] = matId;
         return matId;
      }

      private static ElementId CalcBestMaterialIdFromParameter(Element element)
      {
         ElementId matId = ExporterUtil.GetSingleMaterial(element);
         if (matId != ElementId.InvalidElementId)
         {
            return matId;
         }

         // Try to get it from the category of the element first.
         ElementId categoryId = CategoryUtil.GetSafeCategoryId(element);
         if (CategoryHasMaterialIdParam(categoryId) || element is MEPSystemType)
         {
            ParameterUtil.GetElementIdValueFromElementOrSymbol(element, BuiltInParameter.MATERIAL_ID_PARAM, out matId);
         }
         else if (CategoryHasStructuralMaterialParam(categoryId))
         {
            ParameterUtil.GetElementIdValueFromElementOrSymbol(element, BuiltInParameter.STRUCTURAL_MATERIAL_PARAM, out matId);
         }

         if (matId != ElementId.InvalidElementId)
         {
            return matId;
         }

         // If not, try to get it from the system.
         ElementId systemTypeId = ElementId.InvalidElementId;
         if (IsDuctCategory(categoryId))
         {
            ParameterUtil.GetElementIdValueFromElement(element, BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM, out systemTypeId);
         }
         else if (IsPipeCategory(categoryId))
         {
            ParameterUtil.GetElementIdValueFromElement(element, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM, out systemTypeId);
         }

         if (systemTypeId != ElementId.InvalidElementId)
         {
            Element systemType = element.Document.GetElement(systemTypeId);
            if (systemType != null)
            {
               matId = GetBestMaterialIdFromParameter(systemType);
               return matId;
            }
         }

         return matId;
      }
      
      /// <summary>
      /// Gets the best material id from the geometry or its structural material parameter.
      /// </summary>
      /// <param name="solids">List of solids.</param>
      /// <param name="meshes">List of meshes.</param>
      /// <param name="element">The element.</param>
      /// <returns>The material id.</returns>
      public static ElementId GetBestMaterialIdFromGeometryOrParameter(IList<Solid> solids, IList<Mesh> meshes, Element element)
      {
         ElementId matId = GetBestMaterialIdForGeometry(solids, meshes);
         if (matId == ElementId.InvalidElementId && element != null)
            matId = GetBestMaterialIdFromParameter(element);
         return matId;
      }

      /// <summary>
      /// Gets the best material id from the geometry or its structural material parameter.
      /// </summary>
      /// <param name="geometryElement">The geometry element.</param>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="range">The range to get the clipped geometry.</param>
      /// <param name="element">The element.</param>
      /// <returns>The material id.</returns>
      public static ElementId GetBestMaterialIdFromGeometryOrParameter(GeometryElement geometryElement,
         IFCRange range, Element element)
      {
         ElementId matId = GetBestMaterialIdForGeometry(geometryElement, range);
         if (matId == ElementId.InvalidElementId && element != null)
            matId = GetBestMaterialIdFromParameter(element);
         return matId;
      }

      /// <summary>
      /// Gets the best material id from the geometry or its structural material parameter.
      /// </summary>
      /// <param name="geometryObject">The geometry object.</param>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="element">The element.</param>
      /// <returns>The material id.</returns>
      public static ElementId GetBestMaterialIdFromGeometryOrParameter(GeometryObject geometryObject,
         Element element)
      {
         ElementId matId = GetBestMaterialIdForGeometry(geometryObject);
         if (matId == ElementId.InvalidElementId && element != null)
            matId = GetBestMaterialIdFromParameter(element);
         return matId;
      }

      /// <summary>
      /// Gets a material id from a geometry or element parameters.
      /// </summary>
      /// <param name="geometryObject">The geometry object.</param>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="elementType">The element, usually a type element.</param>
      /// <param name="element">A fallback element to check, usually an instance element.</param>
      /// <returns>The material id.</returns>
      public static ElementId GetBestMaterialIdFromGeometryOrParameter(GeometryObject geometryObject,
         Element elementType, Element element)
      {
         ElementId matId = GetBestMaterialIdFromGeometryOrParameter(geometryObject, elementType);
         if (matId == ElementId.InvalidElementId)
            matId = GetBestMaterialIdFromGeometryOrParameter(geometryObject, element);
         return matId;
      }

      /// <summary>
      /// Gets a list of material ids from the geometry or the element parameters.
      /// </summary>
      /// <param name="geometryObject">The geometry object.</param>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="elementType">The element, usually a type element.</param>
      /// <param name="element">A fallback element to check, usually an instance element.</param>
      /// <returns>The material ids.</returns>
      public static IList<ElementId> GetMaterialIdsFromGeometryOrParameters(GeometryObject geometryObject,
      Element elementType, Element element)
      {
         ElementId matId = GetBestMaterialIdFromGeometryOrParameter(geometryObject, elementType, element);

         // Also get the materials from Parameters
         IList<ElementId> matIds = ParameterUtil.FindMaterialParameters(elementType);
         if (matIds.Count == 0)
            matIds = ParameterUtil.FindMaterialParameters(element);

         // Combine the material ids
         if (matId != ElementId.InvalidElementId && !matIds.Contains(matId))
            matIds.Add(matId);

         return matIds;
      }

      /// <summary>
      /// Creates the related IfcSurfaceStyle for a representation item.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="document">The document.</param>
      /// <param name="isVoid">True if the representation item represents a void (a space or an opening).</param>
      /// <param name="repItemHnd">The representation item.</param>
      /// <param name="overrideMatId">The material id to use instead of the one in the exporter, if provided.</param>
      public static void CreateSurfaceStyleForRepItem(ExporterIFC exporterIFC, Document document,
         bool isVoid, IFCAnyHandle repItemHnd, ElementId overrideMatId)
      {
         if (isVoid || repItemHnd == null || ExporterCacheManager.ExportOptionsCache.ExportAs2x2)
            return;

         // Restrict material to proper subtypes.
         if (!IFCAnyHandleUtil.IsSubTypeOf(repItemHnd, IFCEntityType.IfcSolidModel) &&
             !IFCAnyHandleUtil.IsSubTypeOf(repItemHnd, IFCEntityType.IfcFaceBasedSurfaceModel) &&
             !IFCAnyHandleUtil.IsSubTypeOf(repItemHnd, IFCEntityType.IfcShellBasedSurfaceModel) &&
             !IFCAnyHandleUtil.IsSubTypeOf(repItemHnd, IFCEntityType.IfcSurface) &&
             !IFCAnyHandleUtil.IsSubTypeOf(repItemHnd, IFCEntityType.IfcTessellatedItem))
         {
            throw new InvalidOperationException("Attempting to set surface style for unknown item.");
         }

         IFCFile file = exporterIFC.GetFile();

         ElementId materialId = (overrideMatId != ElementId.InvalidElementId) ? overrideMatId : exporterIFC.GetMaterialIdForCurrentExportState();
         if (materialId == ElementId.InvalidElementId)
            return;

         IFCAnyHandle presStyleHnd = ExporterCacheManager.PresentationStyleAssignmentCache.Find(materialId);
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(presStyleHnd))
         {
            IFCAnyHandle surfStyleHnd = CategoryUtil.GetOrCreateMaterialStyle(document, file, materialId);
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(surfStyleHnd))
               return;

            ISet<IFCAnyHandle> styles = new HashSet<IFCAnyHandle>() { surfStyleHnd };

            if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            {
               presStyleHnd = IFCInstanceExporter.CreatePresentationStyleAssignment(file, styles);
               ExporterCacheManager.PresentationStyleAssignmentCache.Register(materialId, presStyleHnd);
            }
            else
            {
               // IFC4: Using IfcPresentationStyleAssignment is deprecated, use the direct assignment of a subtype of IfcPresentationStyle instead.
               presStyleHnd = surfStyleHnd;
               ExporterCacheManager.PresentationStyleAssignmentCache.Register(materialId, surfStyleHnd);
            }
         }

         if (presStyleHnd != null)
         {
            // WR11: Restricts the number of styles to 1, so create new HashSet with the last one.
            HashSet<IFCAnyHandle> presStyleSet = new HashSet<IFCAnyHandle>() { presStyleHnd };

            // Check if the IfcStyledItem has already been set for this representation item.  If so, don't set it
            // again.  This can happen in BodyExporter in certain cases where we call CreateSurfaceStyleForRepItem twice.
            HashSet<IFCAnyHandle> styledByItemHandles = IFCAnyHandleUtil.GetAggregateInstanceAttribute<HashSet<IFCAnyHandle>>(repItemHnd, "StyledByItem");
            if (styledByItemHandles == null || styledByItemHandles.Count == 0)
            {
               IFCInstanceExporter.CreateStyledItem(file, repItemHnd, presStyleSet, null);
            }
            else
            {
               IFCAnyHandle styledItem = styledByItemHandles.First();
               IFCAnyHandleUtil.SetAttribute(styledItem, "Styles", presStyleSet);
            }
         }
         return;
      }

      /// <summary>
      /// Creates the related IfcCurveStyle for a representation item.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="repItemHnd">The representation item.</param>
      /// <param name="curveWidth">The curve width.</param>
      /// <param name="colorHnd">The curve color handle.</param>
      /// <returns>The IfcCurveStyle handle.</returns>
      public static IFCAnyHandle CreateCurveStyleForRepItem(ExporterIFC exporterIFC, IFCAnyHandle repItemHnd, IFCData curveWidth, IFCAnyHandle colorHnd)
      {
         // Styled Item is not allowed in IFC4RV
         if (repItemHnd == null || ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
            return null;

         IFCFile file = exporterIFC.GetFile();

         IFCAnyHandle curveStyleHnd = IFCInstanceExporter.CreateCurveStyle(file, null, null, curveWidth, colorHnd);
         ISet<IFCAnyHandle> styles = new HashSet<IFCAnyHandle>() { curveStyleHnd };

         if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         {
            IFCAnyHandle presStyleHnd = IFCInstanceExporter.CreatePresentationStyleAssignment(file, styles);
            HashSet<IFCAnyHandle> presStyleSet = new HashSet<IFCAnyHandle>() { presStyleHnd };

            return IFCInstanceExporter.CreateStyledItem(file, repItemHnd, presStyleSet, null);
         }
         else
         {
            return IFCInstanceExporter.CreateStyledItem(file, repItemHnd, styles as HashSet<IFCAnyHandle>, null);
         }
      }

      /// <summary>
      /// Checks if the faces can create a closed shell.
      /// </summary>
      /// <param name="mesh">The mesh.</param>
      /// <returns>True if can, false if can't.</returns>
      public static bool CanCreateClosedShell(Mesh mesh)
      {
         int numFaces = mesh.NumTriangles;

         // Do simple checks first.
         if (numFaces < 4)
            return false;

         // Try to match up edges.
         HashSet<Tuple<uint, uint>> matchedEdges = new();
         Dictionary<uint, IList<uint>> unmatchedEdges = new();
         int unmatchedEdgeSz = 0;

         for (int ii = 0; ii < numFaces; ii++)
         {
            MeshTriangle meshTriangle = mesh.get_Triangle(ii);
            for (int jj = 0; jj < 3; jj++)
            {
               uint pt1 = meshTriangle.get_Index(jj);
               uint pt2 = meshTriangle.get_Index((jj + 1) % 3);

               // Every edge shall be referenced exactly twice by the loops of the face.
               if (matchedEdges.Contains(new Tuple<uint, uint>(pt1, pt2)) ||
                  matchedEdges.Contains(new Tuple<uint, uint>(pt2, pt1)))
                  return false;

               if (unmatchedEdges.TryGetValue(pt2, out IList<uint> unmatchedEdgesPt2) &&
                  unmatchedEdgesPt2.Contains(pt1))
               {
                  unmatchedEdgesPt2.Remove(pt1);
                  matchedEdges.Add(new Tuple<uint, uint>(pt1, pt2));
                  unmatchedEdgeSz--;
               }
               else
               {
                  IList<uint> unmatchedEdgesPt1 = null;
                  if (unmatchedEdges.TryGetValue(pt1, out unmatchedEdgesPt1) && unmatchedEdgesPt1.Contains(pt2))
                  {
                     // An edge with the same orientation exists twice; can't create solid.
                     return false;
                  }

                  if (unmatchedEdgesPt1 == null)
                  {
                     unmatchedEdgesPt1 = new List<uint>();
                     unmatchedEdges[pt1] = unmatchedEdgesPt1;
                  }

                  unmatchedEdgesPt1.Add(pt2);
                  unmatchedEdgeSz++;
               }
            }
         }

         return (unmatchedEdgeSz == 0);
      }

      /// <summary>
      /// Checks if the faces can create a closed shell.
      /// </summary>
      /// <param name="faceSetIndices">The collection of face set indices.</param>
      /// <returns>True if can, false if can't.</returns>
      /// <remarks>This is an optimized version of CanCreateClosedShell(ICollection<IFCAnyHandle> faceSet). </remarks>
      public static bool CanCreateClosedShell(ICollection<IList<IList<int>>> faceSetIndices)
      {
         int numFaces = faceSetIndices.Count;

         // Do simple checks first.
         if (numFaces < 4)
            return false;

         // Try to match up edges.
         HashSet<Tuple<int, int>> matchedEdges = new();
         Dictionary<int, IList<int>> unmatchedEdges = new();
         int unmatchedEdgeSz = 0;

         foreach (IList<IList<int>> face in faceSetIndices)
         {
            foreach (IList<int> points in face)
            {
               int sizeOfBoundary = points.Count;
               if (sizeOfBoundary < 3)
                  return false;

               for (int ii = 0; ii < sizeOfBoundary; ii++)
               {
                  int pt1 = points[ii];
                  int pt2 = points[(ii + 1) % sizeOfBoundary];

                  // Every edge shall be referenced exactly twice by the loops of the face.
                  if (matchedEdges.Contains(new Tuple<int, int>(pt1, pt2)) ||
                     matchedEdges.Contains(new Tuple<int, int>(pt2, pt1)))
                      return false;

                  if (unmatchedEdges.TryGetValue(pt2, out IList<int> unmatchedEdgesPt2) &&
                     unmatchedEdgesPt2.Contains(pt1))
                  {
                     unmatchedEdgesPt2.Remove(pt1);
                     matchedEdges.Add(new Tuple<int, int>(pt1, pt2));
                     unmatchedEdgeSz--;
                  }
                  else
                  {
                     if (unmatchedEdges.TryGetValue(pt1, out IList<int> unmatchedEdgesPt1) &&
                        unmatchedEdgesPt1.Contains(pt2))
                     {
                        // An edge with the same orientation exists twice; can't create solid.
                        return false;
                     }

                     if (unmatchedEdgesPt1 == null)
                     {
                        unmatchedEdgesPt1 = new List<int>();
                        unmatchedEdges[pt1] = unmatchedEdgesPt1;
                     }

                     unmatchedEdgesPt1.Add(pt2);
                     unmatchedEdgeSz++;
                  }
               }
            }
         }

         return (unmatchedEdgeSz == 0);
      }

      /// <summary>
      /// Checks if the faces can create a closed shell.
      /// </summary>
      /// <param name="faceSet">The collection of face handles.</param>
      /// <returns>True if can, false if can't.</returns>
      public static bool CanCreateClosedShell(ICollection<IFCAnyHandle> faceSet)
      {
         int numFaces = faceSet.Count;

         // Do simple checks first.
         if (numFaces < 4)
            return false;

         foreach (IFCAnyHandle face in faceSet)
         {
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(face))
               return false;
         }

         // Try to match up edges.
         HashSet<Tuple<int, int>> matchedEdges = new();
         Dictionary<int, IList<int>> unmatchedEdges = new();
         int unmatchedEdgeSz = 0;

         foreach (IFCAnyHandle face in faceSet)
         {
            HashSet<IFCAnyHandle> currFaceBounds = GeometryUtil.GetFaceBounds(face);
            foreach (IFCAnyHandle boundary in currFaceBounds)
            {
               if (IFCAnyHandleUtil.IsNullOrHasNoValue(boundary))
                  return false;

               IList<IFCAnyHandle> points = GeometryUtil.GetBoundaryPolygon(boundary);
               int sizeOfBoundary = points.Count;
               if (sizeOfBoundary < 3)
                  return false;

               bool reverse = !GeometryUtil.BoundaryHasSameSense(boundary);
               for (int ii = 0; ii < sizeOfBoundary; ii++)
               {
                  int pt1 = reverse ? points[(ii + 1) % sizeOfBoundary].Id : points[ii].Id;
                  int pt2 = reverse ? points[ii].Id : points[(ii + 1) % sizeOfBoundary].Id;

                  // Every edge shall be referenced exactly twice by the loops of the face.
                  if (matchedEdges.Contains(new Tuple<int, int>(pt1, pt2)) ||
                     matchedEdges.Contains(new Tuple<int, int>(pt2, pt1)))
                     return false;

                  if (unmatchedEdges.TryGetValue(pt2, out IList<int> unmatchedEdgesPt2) &&
                     unmatchedEdgesPt2.Contains(pt1))
                  {
                     unmatchedEdgesPt2.Remove(pt1);
                     matchedEdges.Add(new Tuple<int, int>(pt1, pt2));
                     unmatchedEdgeSz--;
                  }
                  else
                  {
                     if (unmatchedEdges.TryGetValue(pt1, out IList<int> unmatchedEdgesPt1) &&
                        unmatchedEdgesPt1.Contains(pt2))
                     {
                        // An edge with the same orientation exists twice; can't create solid.
                        return false;
                     }

                     if (unmatchedEdgesPt1 == null)
                     {
                        unmatchedEdgesPt1 = new List<int>();
                        unmatchedEdges[pt1] = unmatchedEdgesPt1;
                     }

                     unmatchedEdgesPt1.Add(pt2);
                     unmatchedEdgeSz++;
                  }
               }
            }
         }

         return (unmatchedEdgeSz == 0);
      }

      // This is a simplified routine for solids that are composed of planar faces with
      // polygonal edges.  This allows us to use the edges as the boundaries of the faces.
      private static bool ExportPlanarBodyIfPossible(ExporterIFC exporterIFC, Solid solid,
          IList<FaceSetInfo> currentFaceHashSetList, Transform lcs)
      {
         IFCFile file = exporterIFC.GetFile();

         IList<PlanarFace> planarFaces = new List<PlanarFace>();
         foreach (Face face in solid.Faces)
         {
            PlanarFace planarFace = face as PlanarFace;
            if (planarFace == null)
               return false;

            planarFaces.Add(planarFace);
         }

         double vertexTolerance = ExporterCacheManager.Document.Application.VertexTolerance;
         IFCXYZFuzzyComparer ifcXYZFuzzyComparer = new IFCXYZFuzzyComparer(vertexTolerance);
         
         // Prepare polyloop data and check the polyloops for duplicate points before creating
         // IFC instances to avoid unnecessary points and faces in the IFC document.
         IDictionary<PlanarFace, IList<IList<XYZ>>> polyloops = new Dictionary<PlanarFace, IList<IList<XYZ>>>();
         ICollection<IList<IList<int>>> faceSetIndices = new HashSet<IList<IList<int>>>();
         foreach (PlanarFace planarFace in planarFaces)
         {
            EdgeArrayArray edgeArrayArray = planarFace.EdgeLoops;
            IList<IList<XYZ>> edgeArrayVertices = new List<IList<XYZ>>();

            foreach (EdgeArray edgeArray in edgeArrayArray)
            {
               ISet<XYZ> uniqueVertices = new SortedSet<XYZ>(ifcXYZFuzzyComparer);
               IList<XYZ> vertices = new List<XYZ>();

               foreach (Edge edge in edgeArray)
               {
                  Curve curve = edge.AsCurveFollowingFace(planarFace);
                  IList<XYZ> curvePoints = curve.Tessellate();
                  int numPoints = curvePoints.Count;
                  // Don't add last point to vertices, as this will be added in the next edge.
                  for (int idx = 0; idx < numPoints - 1; idx++)
                  {
                     uniqueVertices.Add(curvePoints[idx]);
                     vertices.Add(curvePoints[idx]);
                  }
               }

               // Polyloop contains duplicates or small edges
               if (uniqueVertices.Count < 3)
               {
                  return false;
               }

               edgeArrayVertices.Add(vertices);
            }

            polyloops.Add(planarFace, edgeArrayVertices);
         }

         HashSet<IFCAnyHandle> currentFaceSet = new HashSet<IFCAnyHandle>();
         IDictionary<XYZ, IFCAnyHandle> vertexCache = new SortedDictionary<XYZ, IFCAnyHandle>(ifcXYZFuzzyComparer);
         foreach (KeyValuePair<PlanarFace, IList<IList<XYZ>>> polyloop in polyloops)
         {
            IList<IList<int>> planarFaceIndices = new List<IList<int>>();

            HashSet<IFCAnyHandle> faceBounds = new HashSet<IFCAnyHandle>();
            IList<IList<IFCAnyHandle>> edgeArrayVertices = new List<IList<IFCAnyHandle>>();
            XYZ faceNormal = polyloop.Key.FaceNormal;
            int outerEdgeArrayIndex = 0;
            int edgeArraySize = polyloop.Value.Count;
            double? maxArea = null; // Only used/set if edgeArraySize > 1.

            foreach (IList<XYZ> edgeLoop in polyloop.Value)
            {
               IList<int> edgeLoopIndices = new List<int>();

               IList<IFCAnyHandle> vertices = new List<IFCAnyHandle>();
               IList<XYZ> vertexXYZs = new List<XYZ>();

               foreach (XYZ vertex in edgeLoop)
               {
                  if (!vertexCache.TryGetValue(vertex, out IFCAnyHandle pointHandle))
                  {
                     XYZ scaledPoint = TransformAndScalePoint(exporterIFC, vertex, lcs);
                     pointHandle = ExporterUtil.CreateCartesianPoint(file, scaledPoint);
                     vertexCache[vertex] = pointHandle;
                  }

                  vertices.Add(pointHandle);
                  edgeLoopIndices.Add(pointHandle.Id);
                  vertexXYZs.Add(vertex);
               }

               if (edgeArraySize > 1)
               {
                  double currArea = Math.Abs(GeometryUtil.ComputePolygonalLoopArea(vertexXYZs, faceNormal, vertexXYZs[0]));
                  if (currArea > (maxArea ?? 0.0))
                  {
                     outerEdgeArrayIndex = edgeArrayVertices.Count;
                     maxArea = currArea;
                  }
               }

               edgeArrayVertices.Add(vertices);
               planarFaceIndices.Add(edgeLoopIndices);
            }

            for (int ii = 0; ii < edgeArraySize; ii++)
            {
               if (edgeArrayVertices[ii].Count < 3)
               {
                  // TODO: when we implement logging, log an error here - an invalid edge loop.
                  continue;
               }

               IFCAnyHandle faceLoop = IFCInstanceExporter.CreatePolyLoop(file, edgeArrayVertices[ii]);
               IFCAnyHandle faceBound = (ii == outerEdgeArrayIndex) ?
                   IFCInstanceExporter.CreateFaceOuterBound(file, faceLoop, true) :
                   IFCInstanceExporter.CreateFaceBound(file, faceLoop, true);

               faceBounds.Add(faceBound);
            }

            if (faceBounds.Count > 0)
            {
               IFCAnyHandle currFace = IFCInstanceExporter.CreateFace(file, faceBounds);
               currentFaceSet.Add(currFace);
            }

            faceSetIndices.Add(planarFaceIndices);
         }

         if (currentFaceSet.Count > 0)
         {
            bool canCreateClosedShell = CanCreateClosedShell(faceSetIndices);
            currentFaceHashSetList.Add(new FaceSetInfo(currentFaceSet, canCreateClosedShell));
         }

         return true;
      }

      // This class allows us to merge points that are equal within a small tolerance.
      private class FuzzyPoint
      {
         XYZ m_Point;

         public FuzzyPoint(XYZ point)
         {
            m_Point = point;
         }

         public XYZ Point
         {
            get { return m_Point; }
            set { m_Point = value; }
         }

         static public bool operator ==(FuzzyPoint first, FuzzyPoint second)
         {
            Object lhsObject = first;
            Object rhsObject = second;
            if (null == lhsObject)
            {
               if (null == rhsObject)
                  return true;
               return false;
            }
            if (null == rhsObject)
               return false;

            if (!first.Point.IsAlmostEqualTo(second.Point))
               return false;

            return true;
         }

         static public bool operator !=(FuzzyPoint first, FuzzyPoint second)
         {
            return !(first == second);
         }

         public override bool Equals(object obj)
         {
            if (obj == null)
               return false;

            FuzzyPoint second = obj as FuzzyPoint;
            return (this == second);
         }

         public override int GetHashCode()
         {
            double total = Point.X + Point.Y + Point.Z;
            return Math.Floor(total * 10000.0 + 0.3142).GetHashCode();
         }
      }

      // This class allows us to merge Planes that have normals and origins that are equal within a small tolerance.
      private class PlanarKey
      {
         readonly FuzzyPoint m_Norm;
         readonly FuzzyPoint m_Origin;

         public PlanarKey(XYZ norm, XYZ origin)
         {
            m_Norm = new FuzzyPoint(norm);
            m_Origin = new FuzzyPoint(origin);
         }

         public XYZ Norm
         {
            get { return m_Norm.Point; }
            set { m_Norm.Point = value; }
         }

         public XYZ Origin
         {
            get { return m_Origin.Point; }
            set { m_Origin.Point = value; }
         }

         static public bool operator ==(PlanarKey first, PlanarKey second)
         {
            Object lhsObject = first;
            Object rhsObject = second;
            if (null == lhsObject)
            {
               if (null == rhsObject)
                  return true;
               return false;
            }
            if (null == rhsObject)
               return false;

            if (first.m_Origin != second.m_Origin)
               return false;

            if (first.m_Norm != second.m_Norm)
               return false;

            return true;
         }

         static public bool operator !=(PlanarKey first, PlanarKey second)
         {
            return !(first == second);
         }

         public override bool Equals(object obj)
         {
            if (obj == null)
               return false;

            PlanarKey second = obj as PlanarKey;
            return (this == second);
         }

         public override int GetHashCode()
         {
            return m_Origin.GetHashCode() + m_Norm.GetHashCode();
         }
      }

      // This class contains a listing of the indices of the triangles on the plane, and some simple
      // connection information to speed up sewing.
      private class PlanarInfo
      {
         public IList<int> TriangleList = new List<int>();

         public Dictionary<int, HashSet<int>> TrianglesAtVertexList = new Dictionary<int, HashSet<int>>();

         public void AddTriangleIndexToVertexGrouping(int triangleIndex, int vertex)
         {
            if (TrianglesAtVertexList.TryGetValue(vertex, out HashSet<int> trianglesAtVertex))
               trianglesAtVertex.Add(triangleIndex);
            else
            {
               trianglesAtVertex = new HashSet<int>() { triangleIndex };
               TrianglesAtVertexList[vertex] = trianglesAtVertex;
            }
         }
      }

      // This routine is inefficient, so we will cap how much work we allow it to do.
      private static IList<LinkedList<int>> ConvertTrianglesToPlanarFacets(TriangulatedShellComponent component)
      {
         IList<LinkedList<int>> facets = new List<LinkedList<int>>();

         int numTriangles = component.TriangleCount;

         // sort triangles by normals.

         // This is a list of triangles whose planes are difficult to calculate, so we won't try to optimize them.
         IList<int> sliverTriangles = new List<int>();

         // PlanarKey allows for planes with almost equal normals and origins to be merged.
         Dictionary<PlanarKey, PlanarInfo> planarGroupings = new Dictionary<PlanarKey, PlanarInfo>();

         for (int ii = 0; ii < numTriangles; ii++)
         {
            TriangleInShellComponent currTriangle = component.GetTriangle(ii);

            // Normalize fails if the length is less than 1e-8 or so.  As such, normalilze the vectors
            // along the way to make sure the CrossProduct length isn't too small. 
            int vertex0 = currTriangle.VertexIndex0;
            int vertex1 = currTriangle.VertexIndex1;
            int vertex2 = currTriangle.VertexIndex2;

            XYZ pt1 = component.GetVertex(vertex0);
            XYZ pt2 = component.GetVertex(vertex1);
            XYZ pt3 = component.GetVertex(vertex2);
            XYZ norm = null;

            try
            {
               XYZ xDir = (pt2 - pt1).Normalize();
               norm = xDir.CrossProduct((pt3 - pt1).Normalize());
               norm = norm.Normalize();
            }
            catch
            {
               sliverTriangles.Add(ii);
               continue;
            }

            double distToOrig = norm.DotProduct(pt1);
            XYZ origin = new XYZ(norm.X * distToOrig, norm.Y * distToOrig, norm.Z * distToOrig);

            // Go through map of existing planes and add triangle.
            PlanarKey currKey = new PlanarKey(norm, origin);
            if (planarGroupings.TryGetValue(currKey, out PlanarInfo planarGrouping))
            {
               planarGrouping.TriangleList.Add(ii);
            }
            else
            {
               planarGrouping = new PlanarInfo();
               planarGrouping.TriangleList.Add(ii);
               planarGroupings[currKey] = planarGrouping;
            }

            planarGrouping.AddTriangleIndexToVertexGrouping(ii, vertex0);
            planarGrouping.AddTriangleIndexToVertexGrouping(ii, vertex1);
            planarGrouping.AddTriangleIndexToVertexGrouping(ii, vertex2);
         }

         foreach (PlanarInfo planarGroupingInfo in planarGroupings.Values)
         {
            IList<int> planarGrouping = planarGroupingInfo.TriangleList;

            // All the visited triangles in the current group.
            HashSet<int> visitedTriangles = new HashSet<int>();
            int numCurrTriangles = planarGrouping.Count;

            for (int ii = 0; ii < numCurrTriangles; ii++)
            {
               int idx = planarGrouping[ii];
               if (visitedTriangles.Contains(idx))
                  continue;

               // The visited but not added yet triangles. Used when we have to revert merging.
               HashSet<int> visitedNewTriangles = new HashSet<int>();

               TriangleInShellComponent currTriangle = component.GetTriangle(idx);

               LinkedList<int> currFacet = new LinkedList<int>();
               currFacet.AddLast(currTriangle.VertexIndex0);
               currFacet.AddLast(currTriangle.VertexIndex1);
               currFacet.AddLast(currTriangle.VertexIndex2);

               // If only one triangle, a common case, add the facet and break out.
               if (numCurrTriangles == 1)
               {
                  facets.Add(currFacet);
                  break;
               }

               // If there are too many triangles, we won't try to be fancy until this routine is optimized.
               if (numCurrTriangles > 150)
               {
                  facets.Add(currFacet);
                  continue;
               }

               HashSet<int> currFacetVertices = new HashSet<int>()
               {
                  currTriangle.VertexIndex0,
                  currTriangle.VertexIndex1,
                  currTriangle.VertexIndex2
               };

               visitedTriangles.Add(idx);
               visitedNewTriangles.Add(idx);

               bool foundTriangle;
               do
               {
                  foundTriangle = false;

                  // For each pair of adjacent vertices in the triangle, see if there is a triangle that shares that edge.
                  int sizeOfCurrBoundary = currFacet.Count;
                  foreach (int currVertexIndex in currFacet)
                  {
                     HashSet<int> trianglesAtCurrVertex = planarGroupingInfo.TrianglesAtVertexList[currVertexIndex];
                     foreach (int potentialNeighbor in trianglesAtCurrVertex)
                     {
                        if (visitedTriangles.Contains(potentialNeighbor))
                           continue;

                        TriangleInShellComponent candidateTriangle = component.GetTriangle(potentialNeighbor);
                        int oldVertex = -1, newVertex = -1;

                        // Same normal, unvisited face - see if we have a matching edge.
                        if (currFacetVertices.Contains(candidateTriangle.VertexIndex0))
                        {
                           if (currFacetVertices.Contains(candidateTriangle.VertexIndex1))
                           {
                              oldVertex = candidateTriangle.VertexIndex1;
                              newVertex = candidateTriangle.VertexIndex2;
                           }
                           else if (currFacetVertices.Contains(candidateTriangle.VertexIndex2))
                           {
                              oldVertex = candidateTriangle.VertexIndex0;
                              newVertex = candidateTriangle.VertexIndex1;
                           }
                        }
                        else if (currFacetVertices.Contains(candidateTriangle.VertexIndex1))
                        {
                           if (currFacetVertices.Contains(candidateTriangle.VertexIndex2))
                           {
                              oldVertex = candidateTriangle.VertexIndex2;
                              newVertex = candidateTriangle.VertexIndex0;
                           }
                        }

                        if (oldVertex == -1 || newVertex == -1)
                           continue;

                        // Found a matching edge, insert it into the existing list.
                        LinkedListNode<int> newPosition = currFacet.Find(oldVertex);
                        currFacet.AddAfter(newPosition, newVertex);

                        foundTriangle = true;
                        visitedTriangles.Add(potentialNeighbor);
                        visitedNewTriangles.Add(potentialNeighbor);
                        currFacetVertices.Add(newVertex);

                        break;
                     }

                     if (foundTriangle)
                        break;
                  }
               } while (foundTriangle);

               // Check the validity of the facets.  For now, if we have a duplicated vertex,
               // revert to the original triangles.  TODO: split the facet into outer and inner
               // loops and remove unnecessary edges.
               if (currFacet.Count == currFacetVertices.Count)
                  facets.Add(currFacet);
               else
               {
                  foreach (int visitedIdx in visitedNewTriangles)
                  {
                     TriangleInShellComponent visitedTriangle = component.GetTriangle(visitedIdx);

                     LinkedList<int> visitedFacet = new LinkedList<int>();
                     visitedFacet.AddLast(visitedTriangle.VertexIndex0);
                     visitedFacet.AddLast(visitedTriangle.VertexIndex1);
                     visitedFacet.AddLast(visitedTriangle.VertexIndex2);

                     facets.Add(visitedFacet);
                  }
               }
            }
         }

         // Add in slivery triangles.
         foreach (int sliverIdx in sliverTriangles)
         {
            TriangleInShellComponent currTriangle = component.GetTriangle(sliverIdx);

            LinkedList<int> currFacet = new LinkedList<int>();
            currFacet.AddLast(currTriangle.VertexIndex0);
            currFacet.AddLast(currTriangle.VertexIndex1);
            currFacet.AddLast(currTriangle.VertexIndex2);

            facets.Add(currFacet);
         }

         return facets;
      }

      /// <summary>
      /// Create an IfcFace with one outer loop whose vertices are defined by the vertices array.
      /// </summary>
      /// <param name="file">The file.</param>
      /// <param name="vertices">The vertices.</param>
      /// <returns>An IfcFace handle.</returns>
      public static IFCAnyHandle CreateFaceFromVertexList(IFCFile file, IList<IFCAnyHandle> vertices)
      {
         IList<IFCAnyHandle> cleanedList = new List<IFCAnyHandle>();
         ISet<int> foundIds = new HashSet<int>();

         int count = vertices.Count;
         for (int ii = 0; ii < count; ii++)
         {
            IFCAnyHandle currentVertex = vertices[ii];
            int id = currentVertex.Id;
            if (id == vertices[(ii + 1) % count].Id)
            {
               continue;
            }

            if (foundIds.Contains(id))
            {
               return null;
            }

            foundIds.Add(id);
            cleanedList.Add(currentVertex);
         }

         if (cleanedList.Count < 3)
         {
            return null;
         }

         IFCAnyHandle faceOuterLoop = IFCInstanceExporter.CreatePolyLoop(file, cleanedList);
         IFCAnyHandle faceOuterBound = IFCInstanceExporter.CreateFaceOuterBound(file, faceOuterLoop, true);
         HashSet<IFCAnyHandle> faceBounds = new HashSet<IFCAnyHandle>() { faceOuterBound };
         return IFCInstanceExporter.CreateFace(file, faceBounds);
      }

      private static bool ExportPlanarFacetsIfPossible(IFCFile file, TriangulatedShellComponent component, IList<IFCAnyHandle> vertexHandles,
         HashSet<IFCAnyHandle> currentFaceSet, out bool canExportAsClosedShell)
      {
         canExportAsClosedShell = false;
         IList<LinkedList<int>> facets = null;
         try
         {
            facets = ConvertTrianglesToPlanarFacets(component);
         }
         catch
         {
            return false;
         }

         if (facets == null)
            return false;

         HashSet<IList<IList<int>>> faceSetIndices = new();

         foreach (LinkedList<int> facet in facets)
         {
            List<IList<int>> planarFaceIndices = new();

            IList<IFCAnyHandle> vertices = new List<IFCAnyHandle>();
            int numVertices = facet.Count;
            if (numVertices < 3)
            {
               continue;
            }

            List<int> edgeLoopIndices = new();
            foreach (int vertexIndex in facet)
            {
               vertices.Add(vertexHandles[vertexIndex]);
               edgeLoopIndices.Add(vertexHandles[vertexIndex].Id);
            }

            IFCAnyHandle face = CreateFaceFromVertexList(file, vertices);
            currentFaceSet.AddIfNotNull(face);

            planarFaceIndices.Add(edgeLoopIndices);
            faceSetIndices.Add(planarFaceIndices);
         }

         canExportAsClosedShell = CanCreateClosedShell(faceSetIndices);

         return true;
      }

      private static IFCAnyHandle CreateEdgeCurveFromCurve(IFCFile file, ExporterIFC exporterIFC, Curve curve, IFCAnyHandle edgeStart, IFCAnyHandle edgeEnd,
         bool sameSense, IDictionary<IFCFuzzyXYZ, IFCAnyHandle> cartesianPoints)
      {
         bool allowAdvancedCurve = !ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4;
         const GeometryUtil.TrimCurvePreference trimCurvePreference = GeometryUtil.TrimCurvePreference.BaseCurve;
         IFCAnyHandle baseCurve = GeometryUtil.CreateIFCCurveFromRevitCurve(file, 
            exporterIFC, curve, allowAdvancedCurve, cartesianPoints, trimCurvePreference, null);

         if (IFCAnyHandleUtil.IsNullOrHasNoValue(baseCurve))
            return null;

         IFCAnyHandle edgeCurve = IFCInstanceExporter.CreateEdgeCurve(file, edgeStart, edgeEnd, baseCurve, sameSense);
         return edgeCurve;
      }

      private static IFCAnyHandle CreateProfileCurveFromCurve(IFCFile file, ExporterIFC exporterIFC, Curve curve, string profileName,
         IDictionary<IFCFuzzyXYZ, IFCAnyHandle> cartesianPoints, Transform additionalTrf = null)
      {
         bool allowAdvancedCurve = !ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4;
         const GeometryUtil.TrimCurvePreference trimCurvePreference = GeometryUtil.TrimCurvePreference.TrimmedCurve;
         IFCAnyHandle ifcCurve = GeometryUtil.CreateIFCCurveFromRevitCurve(file, exporterIFC,
            curve, allowAdvancedCurve, cartesianPoints, trimCurvePreference, additionalTrf);
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(ifcCurve))
            return null;
         
         IFCAnyHandle sweptCurve = null;

         bool isBound = false;

         IFCAnyHandle curveStart = null;
         IFCAnyHandle curveEnd = null;

         if (!curve.IsBound)
         {
            isBound = false;
         }
         else
         {
            XYZ startPoint = curve.GetEndPoint(0);
            XYZ endPoint = curve.GetEndPoint(1);

            if (startPoint.IsAlmostEqualTo(endPoint))
            {
               isBound = false;
            }
            else
            {
               curveStart = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, curve.GetEndPoint(0), cartesianPoints, additionalTrf);
               curveEnd = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, curve.GetEndPoint(1), cartesianPoints, additionalTrf);
               isBound = true;
            }
         }

         if (!isBound)
         {
            sweptCurve = IFCInstanceExporter.CreateArbitraryClosedProfileDef(file, IFCProfileType.Curve, profileName, ifcCurve);
         }
         else
         {
            sweptCurve = IFCInstanceExporter.CreateArbitraryOpenProfileDef(file, IFCProfileType.Curve, profileName, ifcCurve);
         }

         return sweptCurve;
      }

      private static void ConvertRevitKnotsToIFCKnots(IList<double> originalKnots, IList<double> ifcKnots, IList<int> ifcKnotMultiplicities)
      {
         int lastIndex = -1;
         double lastValue = 0.0;
         foreach (double originalKnot in originalKnots)
         {
            if (lastIndex >= 0 && MathUtil.IsAlmostEqual(lastValue, originalKnot))
            {
               ifcKnotMultiplicities[lastIndex]++;
            }
            else
            {
               ifcKnots.Add(originalKnot);
               ifcKnotMultiplicities.Add(1);
               lastIndex++;
               lastValue = originalKnot;
            }
         }
      }

      private static IFCAnyHandle CreateNURBSSurfaceFromFace(ExporterIFC exporterIFC, IFCFile file, Face face)
      {
         Toolkit.IFC4.IFCBSplineSurfaceForm surfaceForm;
         if (face is RuledFace)
            surfaceForm = Toolkit.IFC4.IFCBSplineSurfaceForm.RULED_SURF;
         else
            surfaceForm = Toolkit.IFC4.IFCBSplineSurfaceForm.UNSPECIFIED;

         try
         {
            NurbsSurfaceData nurbsSurfaceData = ExportUtils.GetNurbsSurfaceDataForSurface(face.GetSurface());

            int uDegree = nurbsSurfaceData.DegreeU;
            int vDegree = nurbsSurfaceData.DegreeV;

            IList<double> originalKnotsU = nurbsSurfaceData.GetKnotsU();
            IList<double> originalKnotsV = nurbsSurfaceData.GetKnotsV();

            int originalKnotsUSz = originalKnotsU.Count;
            int originalKnotsVSz = originalKnotsV.Count;

            int numControlPointsU = originalKnotsUSz - uDegree - 1;
            int numControlPointsV = originalKnotsVSz - vDegree - 1;
            int numControlPoints = numControlPointsU * numControlPointsV;

            // controlPointsList and weightsData
            IList<XYZ> controlPoints = nurbsSurfaceData.GetControlPoints();
            if (controlPoints.Count != numControlPoints)
               return null;

            IList<double> weights = nurbsSurfaceData.GetWeights();
            if (weights.Count != numControlPoints)
               return null;

            IList<IList<IFCAnyHandle>> controlPointsList = new List<IList<IFCAnyHandle>>();
            IList<IList<double>> weightsData = new List<IList<double>>();

            int indexU = 0;
            int indexV = 0;
            int controlPointListSize = 0;
            for (int ii = 0; ii < numControlPoints; ii++)
            {
               IFCAnyHandle controlPointHnd = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, controlPoints[ii], null);
               if (indexU == controlPointListSize)
               {
                  controlPointsList.Add(new List<IFCAnyHandle>());
                  weightsData.Add(new List<double>());
                  controlPointListSize++;
               }

               controlPointsList[indexU].Add(controlPointHnd);
               weightsData[indexU].Add(weights[ii]);

               indexV++;
               if (indexV == numControlPointsV)
               {
                  indexU++;
                  indexV = 0;
               }
            }

            // uKnots and uMultiplicities
            IList<double> uKnots = new List<double>();
            IList<int> uMultiplicities = new List<int>();

            ConvertRevitKnotsToIFCKnots(originalKnotsU, uKnots, uMultiplicities);

            // vKnots and vMultiplicities
            IList<double> vKnots = new List<double>();
            IList<int> vMultiplicities = new List<int>();


            ConvertRevitKnotsToIFCKnots(originalKnotsV, vKnots, vMultiplicities);

            // Rest of values.
            IFCLogical uClosed = IFCLogical.False;
            IFCLogical vClosed = IFCLogical.False;
            IFCLogical selfIntersect = IFCLogical.Unknown;

            Toolkit.IFC4.IFCKnotType knotType = Toolkit.IFC4.IFCKnotType.UNSPECIFIED;

            return IFCInstanceExporter.CreateRationalBSplineSurfaceWithKnots(file, uDegree, vDegree,
               controlPointsList, surfaceForm, uClosed, vClosed, selfIntersect,
               uMultiplicities, vMultiplicities, uKnots, vKnots, knotType, weightsData);
         }
         catch
         {
            return null;
         }
      }

      private static IFCAnyHandle CreatePositionForFace(ExporterIFC exporterIFC, IFCAnyHandle location, XYZ zdir, XYZ xdir)
      {
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(location) || zdir == null || xdir == null)
            return null;

         IFCAnyHandle axisHandle = GeometryUtil.VectorToIfcDirection(exporterIFC, zdir);
         IFCAnyHandle refDirection = GeometryUtil.VectorToIfcDirection(exporterIFC, xdir);
         IFCAnyHandle position = IFCInstanceExporter.CreateAxis2Placement3D(exporterIFC.GetFile(), location, axisHandle, refDirection);
         return position;
      }

      /// <summary>
      /// Returns not-singular UV point for given Revit Face or null if function fails.
      /// </summary>
      /// <param name="face">face</param>
      /// <param name="testPointUV">UV test point created here and returned as out param</param>
      private static void GetNonSingularUVPointForRevitFace(Face face, out UV revitTestUV)
      {
         // The point we use to test will be the middle point of the first "not short" edge of this face

         revitTestUV = null;

         EdgeArrayArray faceEdgeLoops = face.EdgeLoops;
         if (faceEdgeLoops == null || faceEdgeLoops.Size == 0)
            return;
         for (int ii = 0; ii < faceEdgeLoops.Size && revitTestUV == null; ii++)
         {
            EdgeArray edgeLoop = faceEdgeLoops.get_Item(ii);
            if (edgeLoop == null)
               continue;                               // is it possible case?
            for (int jj = 0; jj < edgeLoop.Size && revitTestUV == null; jj++)
            {
               Edge edge = edgeLoop.get_Item(jj);
               if (edge == null)
                  continue;
               // TO DO Find possibility to indicate short edges to skip. 
               //               if (< edge is too short>)               
               //                  continue;
               revitTestUV = edge.EvaluateOnFace(0.5, face);
            }
         }
         return;
      }

      /// <summary>
      /// Uses information about surface of revolution (including Cylinder and Cone) to realize if it is right-handed one or not.
      /// </summary>
      /// <param name="testPointXYZ">XYZ test point on the surface</param>
      /// <param name="axis">surface's axis</param>
      /// <param name="origin">surface's origin</param>
      /// <param name="uDeriv">u derivative in the testPoint</param>
      /// <returns> bool == true if coordinate system is right-handed, otherwise retuns false</returns>
      private static bool IsRightHanded(XYZ testPoint, XYZ axis, XYZ origin, XYZ uDeriv)
      {
         XYZ testDir = (testPoint - origin);

         // This is the rotation direction at the test point for a right handed system
         XYZ tetsDeriv = axis.CrossProduct(testDir);
         // uDeriv is the actual rotation direction
         return tetsDeriv.DotProduct(uDeriv) > 0.0;
      }

      /// <summary>
      /// Returns a handle for creation of an AdvancedBrep with AdvancedFace and assigns it to the file
      /// </summary>
      /// <param name="exporterIFC">exporter IFC</param>
      /// <param name="element">the element</param>
      /// <param name="options">exporter option</param>
      /// <param name="geomObject">the geometry object</param>
      /// <returns>the handle</returns>
      public static IFCAnyHandle ExportBodyAsAdvancedBrep(ExporterIFC exporterIFC, Element element,
          GeometryObject geomObject)
      {
         IFCFile file = exporterIFC.GetFile();
         Document document = element.Document;

         // IFCFuzzyXYZ will be used in this routine to compare 2 XYZs, we consider two points are the same if their distance
         // is within this tolerance
         IFCFuzzyXYZ.IFCFuzzyXYZEpsilon = document.Application.VertexTolerance;

         IFCAnyHandle advancedBrep = null;

         using (IFCTransaction tr = new IFCTransaction(file))
         {
            try
            {
               if (!(geomObject is Solid))
               {
                  return null;
               }
               HashSet<IFCAnyHandle> cfsFaces = new HashSet<IFCAnyHandle>();
               Solid geomSolid = geomObject as Solid;
               FaceArray faces = geomSolid.Faces;

               // Check for supported curve and face types before creating an advanced BRep.
               IList<KeyValuePair<Edge, Curve>> edgesAndCurves = new List<KeyValuePair<Edge, Curve>>();
               foreach (Edge edge in geomSolid.Edges)
               {
                  Curve currCurve = edge.AsCurve();
                  if (currCurve == null)
                     return null;

                  bool isValidCurve = !(currCurve is CylindricalHelix);
                  if (!isValidCurve)
                     return null;

                  // based on the definition of IfcAdvancedBrep in IFC 4 specification, an IfcAdvancedBrep must contain a closed shell, so we
                  // have a test to reject all open shells here.
                  // we check that geomSolid is an actual solid and not an open shell by verifying that each edge is shared by exactly 2 faces.
                  for (int ii = 0; ii < 2; ii++)
                  {
                     if (edge.GetFace(ii) == null)
                        return null;
                  }

                  edgesAndCurves.Add(new KeyValuePair<Edge, Curve>(edge, currCurve));
               }

               foreach (Face face in geomSolid.Faces)
               {
                  bool isValidFace = (face is PlanarFace) || (face is CylindricalFace) || (face is RuledFace) || (face is HermiteFace) || (face is RevolvedFace) || (face is ConicalFace);
                  if (!isValidFace)
                  {
                     return null;
                  }
               }

               Dictionary<Face, IList<Edge>> faceToEdges = new Dictionary<Face, IList<Edge>>();
               Dictionary<Edge, IFCAnyHandle> edgeToIfcEdgeCurve = new Dictionary<Edge, IFCAnyHandle>();

               // A map of already created IfcCartesianPoints, to avoid duplication.  This is used for vertex points and other geometry in the BRep.
               // We do not share IfcCartesianPoints across BReps.
               IDictionary<IFCFuzzyXYZ, IFCAnyHandle> cartesianPoints = new SortedDictionary<IFCFuzzyXYZ, IFCAnyHandle>();

               // A map of already created IfcVertexPoints, to avoid duplication.
               IDictionary<IFCFuzzyXYZ, IFCAnyHandle> vertices = new SortedDictionary<IFCFuzzyXYZ, IFCAnyHandle>();

               // First phase: get all the vertices:
               foreach (KeyValuePair<Edge, Curve> edgeAndCurve in edgesAndCurves)
               {
                  Edge edge = edgeAndCurve.Key;
                  Curve currCurve = edgeAndCurve.Value;

                  // Note that currCurve's parameter bounds may extend beyond the edge.
                  // This is allowed since the edge's start and end points are also used
                  // as part of the overall definition of the IFC edge. At present (February 2018),
                  // this does in fact happen, since convertCurveToGNurbSpline ignores a Hermite spline
                  // curve's bounds (though it shouldn't - see JIRA item REVIT-125815 and related items).
                  // In light of this, we must use the edge's start and end points, not currCurve's
                  // start and end points.
                  //
                  // Also note that this code seems to be intended to use an edge's parametric orientation,
                  // as opposed to its topological orientation on one of its faces (given that it's
                  // processing the list of edges independently of faces).
                  XYZ startPoint = edge.Evaluate(0.0);
                  XYZ endPoint = edge.Evaluate(1.0);

                  IFCFuzzyXYZ fuzzyStartPoint = new IFCFuzzyXYZ(startPoint);
                  IFCFuzzyXYZ fuzzyEndPoint = new IFCFuzzyXYZ(endPoint);
                  IFCAnyHandle edgeStart = null;
                  IFCAnyHandle edgeEnd = null;

                  if (vertices.ContainsKey(fuzzyStartPoint))
                  {
                     edgeStart = vertices[fuzzyStartPoint];
                  }
                  else
                  {
                     IFCAnyHandle edgeStartCP = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, startPoint, cartesianPoints);
                     edgeStart = IFCInstanceExporter.CreateVertexPoint(file, edgeStartCP);
                     vertices.Add(fuzzyStartPoint, edgeStart);
                  }

                  if (vertices.ContainsKey(fuzzyEndPoint))
                  {
                     edgeEnd = vertices[fuzzyEndPoint];
                  }
                  else
                  {
                     IFCAnyHandle edgeEndCP = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, endPoint, cartesianPoints);
                     edgeEnd = IFCInstanceExporter.CreateVertexPoint(file, edgeEndCP);
                     vertices.Add(fuzzyEndPoint, edgeEnd);
                  }

                  IFCAnyHandle edgeCurve = CreateEdgeCurveFromCurve(file, exporterIFC, currCurve, edgeStart, edgeEnd, true, cartesianPoints);

                  edgeToIfcEdgeCurve.Add(edge, edgeCurve);

                  Face face = null;
                  for (int ii = 0; ii < 2; ii++)
                  {
                     face = edge.GetFace(ii);
                     if (!faceToEdges.ContainsKey(face))
                     {
                        faceToEdges.Add(face, new List<Edge>());
                     }
                     IList<Edge> edges = faceToEdges[face];
                     edges.Add(edge);
                  }
               }

               // Second phase: create IfcFaceOuterBound, IfcFaceInnerBound, IfcAdvancedFace and IfcAdvancedBrep
               foreach (Face face in geomSolid.Faces)
               {
                  // List of created IfcEdgeLoops of this face
                  IList<IFCAnyHandle> edgeLoopList = new List<IFCAnyHandle>();
                  // List of created IfcOrientedEdge in one loop
                  IList<IFCAnyHandle> orientedEdgeList = new List<IFCAnyHandle>();
                  IFCAnyHandle surface = null;
                  // We are creating at first IFC Surface and then IFC Face. IFC Face should have the same orientation(normal) as Revit face.
                  // The IFC face has the SameSense flag(sameSenseAF), which tells if the IFC Face Normal is the same or opposite of the IFC Surface Normal. 
                  // If IFC Surface has same normal direction (ifcSurfaceNormalDir) as Revit Face (revitFaceNormal) we are setting sameSenseAF to True. 
                  // Otherwise to False. See the code below just before IFC Face creation. 
                  // Note: All Revit surfaces may be right-handed. Some of them may be left-handed too as well as right-handed. IFC surfaces are always right-handed. 
                  // In case when Revit surface is right-handed IFC Surface Normal match the Revit Surface Parametric Normal. Otherwise they are opposite ones.
                  // Note: instead of IFC surface normal we are using vector ifcSurfaceNormalDir with the same direction but not normalized.
                  // Note: We will compute the ifcSurfaceNormalDir as Revit Surface Parametric Normal direction using cross product of Revit Surface derivatives.
                  // For left-handed Revit Surfaces we flip ifcSurfaceNormalDir. 
                  // Note: Instead of checking that revitFaceNormal and IFCSurfaceNormal are exactly same we are checking if the dot product 
                  // of revitSurfaceNormal and ifcSurfaceNormalDir is positive or not (see below).

                  IList<HashSet<IFCAnyHandle>> boundsCollection = new List<HashSet<IFCAnyHandle>>();

                  // calculate sameSense by getting a point on the Revit Surface and compute the normal of the Revit Face and the surface at that point
                  // if these two vectors agree, then sameSense is true.
                  // The point we use to test will be the middle point of the first appropriate edge in the in all loops of Revit Face
                  GetNonSingularUVPointForRevitFace(face, out UV revitTestUV);
                  if (revitTestUV == null)
                  {
                     return null;
                  }
                  // Compute the normal of the FACE at revitTestUV
                  XYZ revitFaceNormal = face.ComputeNormal(revitTestUV);
                  // Compute the normal of the SURFACE at revitTestUV
                  Transform testPointDerivatives = face.ComputeDerivatives(revitTestUV);
                  XYZ ifcSurfaceNormalDir = testPointDerivatives.BasisX.CrossProduct(testPointDerivatives.BasisY); // May be modified below.
                  if (ifcSurfaceNormalDir.IsZeroLength())
                  {
                     return null;
                  }

                  var sortedEdgeLoop = GeometryUtil.GetOuterLoopsWithInnerLoops(face);
                  if (sortedEdgeLoop == null)
                  {
                     return null;
                  }

                  // check that we get back the same number of edgeloop
                  int numberOfSortedEdgeLoop = 0;

                  foreach (var (outerLoop, innerLoops) in sortedEdgeLoop)
                  {
                     numberOfSortedEdgeLoop += 1 + innerLoops.Count;
                  }

                  if (numberOfSortedEdgeLoop != face.EdgeLoops.Size)
                  {
                     return null;
                  }

                  foreach (var (outerLoop, loops) in sortedEdgeLoop)
                  {
                     if (outerLoop == null || loops == null)
                        return null;

                     HashSet<IFCAnyHandle> bounds = new HashSet<IFCAnyHandle>();

                     // Append the outerloop at the beginning of the list of inner loop
                     loops.Insert(0, outerLoop);

                     // Process each inner loop
                     foreach (EdgeArray edgeArray in loops)
                     {
                        // Map each edge in this loop back to its corresponding edge curve and then calculate its orientation to create IfcOrientedEdge
                        foreach (Edge edge in edgeArray)
                        {
                           // The reason why edgeToIfcEdgeCurve cannot find edge is that either we haven't created the IfcOrientedEdge
                           // corresponding to that edge OR we already have but the dictionary cannot find the edge as its key because 
                           // Face.EdgeLoop and geomSolid.Edges return different pointers for the same edge. This can be avoided if 
                           // Equals() method is implemented for Edge
                           if (!edgeToIfcEdgeCurve.ContainsKey(edge))
                              return null;

                           IFCAnyHandle edgeCurve = edgeToIfcEdgeCurve[edge];

                           Curve currCurve = edge.AsCurve();
                           Curve curveInCurrentFace = edge.AsCurveFollowingFace(face);

                           if (currCurve == null || curveInCurrentFace == null)
                              return null;

                           // if the curve length is 0, ignore it.
                           if (MathUtil.IsAlmostZero(currCurve.ApproximateLength))
                              continue;

                           // if the curve is unbound, it means that the solid may be corrupted, we shouldn't process it anymore
                           if (!currCurve.IsBound)
                              return null;

                           // Manually comparing the curves' start points isn't ideal,
                           // though it will usually suffice in practice. Instead, AsCurveFollowingFace
                           // should optionally indicate if its output curve has the same or opposite
                           // orientation as the curve returned by AsCurve. Note that the curves returned
                           // by those two functions are the same (to within Revit's tolerance) up to a
                           // reversal of orientation.
                           bool orientation = currCurve.GetEndPoint(0).IsAlmostEqualTo(curveInCurrentFace.GetEndPoint(0));

                           IFCAnyHandle orientedEdge = IFCInstanceExporter.CreateOrientedEdge(file, edgeCurve, orientation);
                           orientedEdgeList.Add(orientedEdge);
                        }

                        IFCAnyHandle edgeLoop = IFCInstanceExporter.CreateEdgeLoop(file, orientedEdgeList);
                        edgeLoopList.Add(edgeLoop);

                        IFCAnyHandle faceBound = null;

                        // EdgeLoopList has only 1 element indicates that this is the outer loop
                        if (edgeLoopList.Count == 1)
                           faceBound = IFCInstanceExporter.CreateFaceOuterBound(file, edgeLoop, true);
                        else
                        {
                           //Outer loops go CCW around face normal, inner loops CW. Orientation flag on IfcFaceBound
                           //does not indicate this orientation. Rather, it indicates whether or not the loop is used
                           //by the face the way it was first created. Since we create a new IfcFaceBound for each revit
                           //face loop and since each revit loop is oriented w.r.t. the face normal as demanded by IFC,
                           //this is always the case.
                           faceBound = IFCInstanceExporter.CreateFaceBound(file, edgeLoop, true);
                        }

                        bounds.Add(faceBound);

                        // After finishing processing one loop, clear orientedEdgeList
                        orientedEdgeList.Clear();
                     }
                     boundsCollection.Add(bounds);
                  }

                  edgeLoopList.Clear();

                  // TODO: create a new face processing method to factor out this code
                  // process the face now
                  if (face is PlanarFace)
                  {
                     PlanarFace plFace = face as PlanarFace;
                     IFCAnyHandle location = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, plFace.Origin, cartesianPoints);

                     // We create IFC Plane with the same normal as Revit face.
                     XYZ zdir = ifcSurfaceNormalDir = revitFaceNormal; //  plFace.FaceNormal;
                     XYZ xdir = plFace.XVector;

                     IFCAnyHandle position = CreatePositionForFace(exporterIFC, location, zdir, xdir);

                     surface = IFCInstanceExporter.CreatePlane(file, position);
                  }
                  else if (face is CylindricalFace)
                  {
                     // get radius-x and axis vectors and the position of the origin
                     CylindricalFace cylFace = face as CylindricalFace;
                     XYZ origin = cylFace.Origin;
                     IFCAnyHandle location = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, origin, cartesianPoints);

                     XYZ rad = UnitUtil.ScaleLength(cylFace.get_Radius(0));
                     double radius = rad.GetLength();

                     XYZ zdir = cylFace.Axis;
                     XYZ xdir = rad.Normalize();

                     IFCAnyHandle position = CreatePositionForFace(exporterIFC, location, zdir, xdir);

                     surface = IFCInstanceExporter.CreateCylindricalSurface(file, position, radius);
                     bool isRightHanded = IsRightHanded(testPointDerivatives.Origin, zdir, origin, testPointDerivatives.BasisX);
                     ifcSurfaceNormalDir *= isRightHanded ? 1.0 : -1.0;
                  }
                  else if (face is ConicalFace)
                  {
                     ConicalFace conicalFace = face as ConicalFace;
                     XYZ origin = conicalFace.Origin;
                     IFCAnyHandle location = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, origin, cartesianPoints);

                     XYZ zdir = conicalFace.Axis;
                     if (zdir == null)
                        return null;

                     // Create a finite profile curve for the cone based on the bounding box.
                     BoundingBoxUV coneUV = conicalFace.GetBoundingBox();
                     if (coneUV == null)
                        return null;

                     XYZ startPoint = conicalFace.Evaluate(new UV(0, coneUV.Min.V));
                     XYZ endPoint = conicalFace.Evaluate(new UV(0, coneUV.Max.V));

                     Curve profileCurve = Line.CreateBound(startPoint, endPoint);

                     IFCAnyHandle axis = GeometryUtil.VectorToIfcDirection(exporterIFC, zdir);

                     IFCAnyHandle axisPosition = IFCInstanceExporter.CreateAxis1Placement(file, location, axis);

                     IFCAnyHandle sweptCurve = CreateProfileCurveFromCurve(file, exporterIFC, profileCurve, Resources.ConicalFaceProfileCurve, cartesianPoints);
                     if (IFCAnyHandleUtil.IsNullOrHasNoValue(sweptCurve))
                     {
                        return null;
                     }

                     // The profile position is optional in IFC4+.
                     surface = IFCInstanceExporter.CreateSurfaceOfRevolution(file, sweptCurve, null, axisPosition);
                     bool isRightHanded = IsRightHanded(testPointDerivatives.Origin, zdir, origin, testPointDerivatives.BasisX);
                     ifcSurfaceNormalDir *= isRightHanded ? 1.0 : -1.0;
                  }
                  else if (face is RevolvedFace)
                  {
                     RevolvedFace revFace = face as RevolvedFace;
                     XYZ origin = revFace.Origin;
                     IFCAnyHandle location = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, origin, cartesianPoints);

                     XYZ zdir = revFace.Axis;
                     if (zdir == null)
                        return null;

                     // Note that the returned curve is in the coordinate system of the face.
                     Curve curve = revFace.Curve;
                     if (curve == null)
                        return null;

                     // Create arbitrary plane with z direction as normal.
                     Plane arbitraryPlane = GeometryUtil.CreatePlaneByNormalAtOrigin(zdir);

                     Transform revitTransform = Transform.Identity;
                     revitTransform.BasisX = arbitraryPlane.XVec;
                     revitTransform.BasisY = arbitraryPlane.YVec;
                     revitTransform.BasisZ = zdir;
                     revitTransform.Origin = origin;
                     Curve profileCurve = curve.CreateTransformed(revitTransform);

                     IFCAnyHandle axis = GeometryUtil.VectorToIfcDirection(exporterIFC, zdir);

                     IFCAnyHandle axisPosition = IFCInstanceExporter.CreateAxis1Placement(file, location, axis);

                     IFCAnyHandle sweptCurve = CreateProfileCurveFromCurve(file, exporterIFC, profileCurve, Resources.RevolvedFaceProfileCurve, cartesianPoints);
                     if (IFCAnyHandleUtil.IsNullOrHasNoValue(sweptCurve))
                     {
                        return null;
                     }

                     // The profile position is optional in IFC4+.
                     surface = IFCInstanceExporter.CreateSurfaceOfRevolution(file, sweptCurve, null, axisPosition);
                     bool isRightHanded = IsRightHanded(testPointDerivatives.Origin, zdir, origin, testPointDerivatives.BasisX);
                     ifcSurfaceNormalDir *= isRightHanded ? 1.0 : -1.0;
                  }
                  else if (face is RuledFace)
                  {
                     RuledFace ruledFace = face as RuledFace;
                     // If this face is an extruded ruled face, we will export it as an IfcSurfaceOfLinearExtrusion, else, we will
                     // convert it to NURBS surface and then export it to IFC as IfcBSplineSurface
                     if (ruledFace.IsExtruded)
                     {
                        // To create an IFCSurfaceOfLinearExtrusion, we need to know the profile curve, the extrusion direction
                        // and the depth of the extrusion, (position is optional)
                        // To calculate the extrusion direction and the extrusion depth we first get the start points of the 2 
                        // profile curves. The vector connecting these two points will represent the extrusion direction while
                        // its length will be the extrusion depth
                        Curve firstProfileCurve = ruledFace.get_Curve(0);
                        Curve secondProfileCurve = ruledFace.get_Curve(1);
                        if (firstProfileCurve == null || secondProfileCurve == null)
                        {
                           // If IsExtruded is true then both profile curves have to exist, but if one of them is null then the 
                           // input is invalid, reject here
                           return null;
                        }

                        // For the position of the surface, we set its location to be the start point of the first curve, its 
                        // axis to be the extrusion direction and its ref direction to be the 90-degree rotaion of the extrusion
                        // direction
                        IFCAnyHandle location = GeometryUtil.XYZtoIfcCartesianPoint(exporterIFC, firstProfileCurve.GetEndPoint(0), cartesianPoints);

                        XYZ xDir = (firstProfileCurve.GetEndPoint(1) - firstProfileCurve.GetEndPoint(0)).Normalize();
                        XYZ v2 = xDir;
                        double paramStart = 1.0;
                        while (xDir.IsAlmostEqualTo(v2))
                        {
                           paramStart /= 2;
                           v2 = (firstProfileCurve.Evaluate(paramStart, true) - firstProfileCurve.GetEndPoint(0)).Normalize();
                        }
                        XYZ zdir = xDir.CrossProduct(v2).Normalize();

                        IFCAnyHandle sweptCurvePosition = CreatePositionForFace(exporterIFC, location, zdir, xDir);

                        // Set the base plane of the swept curve transform
                        Transform basePlaneTrf = Transform.Identity;
                        var xDirIFC = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, xDir);
                        var zDirIFC = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, zdir);
                        basePlaneTrf.BasisZ = zDirIFC;
                        basePlaneTrf.BasisX = xDirIFC;
                        basePlaneTrf.BasisY = zDirIFC.CrossProduct(xDirIFC);

                        IList<double> locationOrds = IFCAnyHandleUtil.GetCoordinates(location);
                        basePlaneTrf.Origin = new XYZ(locationOrds[0], locationOrds[1], locationOrds[2]);

                        // Transform the dir to follow to the face transform
                        XYZ endsDiff = secondProfileCurve.GetEndPoint(0) - firstProfileCurve.GetEndPoint(0);

                        double depth = UnitUtil.ScaleLength(endsDiff.GetLength());

                        XYZ dir = endsDiff.Normalize();
                        if (dir == null || MathUtil.IsAlmostZero(dir.GetLength()))
                        {
                           // The extrusion direction is either null or too small to normalize
                           return null;
                        }
                        var dirIFC = ExporterIFCUtils.TransformAndScaleVector(exporterIFC, dir);
                        dirIFC = basePlaneTrf.Inverse.OfVector(dirIFC);

                        IFCAnyHandle direction = ExporterUtil.CreateDirection(file, dirIFC);
                        IFCAnyHandle sweptCurve = CreateProfileCurveFromCurve(file, exporterIFC, firstProfileCurve, Resources.RuledFaceProfileCurve, cartesianPoints, basePlaneTrf.Inverse);
                        if (IFCAnyHandleUtil.IsNullOrHasNoValue(sweptCurve))
                        {
                           return null;
                        }

                        surface = IFCInstanceExporter.CreateSurfaceOfLinearExtrusion(file, sweptCurve, sweptCurvePosition, direction, depth);
                     }
                     else
                     {
                        surface = CreateNURBSSurfaceFromFace(exporterIFC, file, face);
                     }
                  }
                  else if (face is HermiteFace)
                  {
                     surface = CreateNURBSSurfaceFromFace(exporterIFC, file, face);
                  }
                  else
                  {
                     return null;
                  }

                  // If we had trouble creating a surface, stop trying.
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(surface))
                     return null;


                  // For Revit surfaces that are converted to IFC spline surfaces or IFC extrusions, CreateNURBSSurfaceFromFace and CreateSurfaceOfLinearExtrusion create a IFC surfaces with the same 
                  // parametric orientation as the parametric orientation of the initial Revit surface. Combined with the fact that IFC surfaces always use their parametric orientation, 
                  // this implies that the IFC surface oriented normal is equal to the Revit surface's parametric normal, which is what ifcSurfaceNormalDir has been set to for spline surface.                  
                  bool sameSenseAF = revitFaceNormal.DotProduct(ifcSurfaceNormalDir) > 0.0;
                  foreach (HashSet<IFCAnyHandle> faceBound in boundsCollection)
                  {
                     IFCAnyHandle advancedFace = IFCInstanceExporter.CreateAdvancedFace(file, faceBound, surface, sameSenseAF);
                     cfsFaces.Add(advancedFace);
                  }
               }

               // create advancedBrep
               IFCAnyHandle closedShell = IFCInstanceExporter.CreateClosedShell(file, cfsFaces);
               advancedBrep = IFCInstanceExporter.CreateAdvancedBrep(file, closedShell);

               if (IFCAnyHandleUtil.IsNullOrHasNoValue(advancedBrep))
                  tr.RollBack();
               else
                  tr.Commit();

               return advancedBrep;
            }
            catch
            {
               return null;
            }
         }
      }

      private static IList<GeometryObject> GetGeometriesFromGeometryElement(
         ExporterIFC exporterIFC, Document document,
         GeometryObject geomObject, bool hideInvisible, out bool allNotToBeExported)
      {
         // TODO: Determine why for polygonal face sets hideInvisible is true, but for
         // triangulated face sets, fideInvisible is false.
         IList<Solid> solidGeom = new List<Solid>();
         IList<Mesh> meshGeom = new List<Mesh>();
         bool geometryToSort = false;

         // If the geomObject is GeometryELement or GeometryInstance, we need to collect their primitive Solid and Mesh first
         allNotToBeExported = false;
         List<GeometryObject> geomObjectPrimitives = new List<GeometryObject>();
         if (geomObject is GeometryElement)
         {
            geomObjectPrimitives.AddRange(GetGeometryObjectListFromGeometryElement(document, exporterIFC, geomObject as GeometryElement, out allNotToBeExported));
         }
         else if (geomObject is GeometryInstance)
         {
            GeometryInstance geomInst = geomObject as GeometryInstance;
            geomObjectPrimitives.AddRange(GetGeometryObjectListFromGeometryElement(document, exporterIFC, geomInst.GetInstanceGeometry(), out allNotToBeExported));
         }
         else if (geomObject is Solid)
         {
            if (hideInvisible)
            {
               solidGeom.Add(geomObject as Solid);
               geometryToSort = true;
            }
            else
            {
               geomObjectPrimitives.Add(geomObject);
            }
         }
         else if (geomObject is Mesh)
         {
            if (hideInvisible)
            {
               meshGeom.Add(geomObject as Mesh);
               geometryToSort = true;
            }
            else
            {
               geomObjectPrimitives.Add(geomObject);
            }
         }

         if (hideInvisible && geometryToSort)
         {
            IList<GeometryObject> visibleSolids = FamilyExporterUtil.RemoveInvisibleSolidsAndMeshes(document, exporterIFC, ref solidGeom, ref meshGeom);
            if (visibleSolids != null && visibleSolids.Count > 0)
               geomObjectPrimitives.AddRange(visibleSolids);
            else
               allNotToBeExported = true;
         }

         return geomObjectPrimitives;
      }

      private static IEnumerable<PlanarFace> GetDataForExportPlanarSolidAsPolygonalFaceSet(Solid solid)
      {
         IEnumerable<PlanarFace> faces = solid.Faces.OfType<PlanarFace>();
         if (faces.Count() != solid.Faces.Size) // Not all faces in the solid were planar
            return null;

         return faces;
      }

      private static (IFCAnyHandle, int) ExportPlanarSolidAsPolygonalFaceSet(ExporterIFC exporterIFC,
         IEnumerable<PlanarFace> faces, Transform trfToUse)
      {
         if (faces == null)
         {
            return (null, 0);
         }

         IFCFile file = exporterIFC.GetFile();
         bool isClosed = true;

         List<IFCAnyHandle> ifcFaceHandles = new List<IFCAnyHandle>();
         // List of all unique vertex XYZs. Each item in this list is an average of all XYZs obtained from adjacent edge end-points.
         List<XYZ> vertexPositions = new List<XYZ>();
         // Dictionary that relates end-points of each edge to an index pointing to a vertex position in the vertexPositions list
         Dictionary<EdgeEndPoint, int> vertexInfoCache = new Dictionary<EdgeEndPoint, int>(new GeometryUtil.EdgeEndPointComparer());
         // List of loops to export with outer loop index
         List<Tuple<IList<IList<int>>, int>> loopsCache = new List<Tuple<IList<IList<int>>, int>>();

         try
         {
            foreach (PlanarFace face in faces)
            {
               // Used to determine which loop is the outer one
               double maxArea = 0.0;
               int outerEdgeLoopIndex = 0;

               // Loops will be collected as lists of 1-based indices that point to vertex positions in the "vertexPositions" list
               List<IList<int>> indexedLoops = new List<IList<int>>();
               foreach (EdgeArray loop in face.EdgeLoops)
               {
                  List<int> indexedLoop = new List<int>();
                  foreach (Edge edge in loop)
                  {
                     // This function isn't applicable to breps with non-linear curves
                     if (!(edge.AsCurve() is Line))
                        return (null, 0);

                     isClosed = isClosed && (edge.GetFace(0) != null && edge.GetFace(1) != null);

                     int ifcVertexIdx = -1;

                     // Process the vertex at the topological start of this edge on the current face
                     int endIdx = edge.IsFlippedOnFace(face) ? 1 : 0;
                     EdgeEndPoint endPnt = new EdgeEndPoint(edge, endIdx);
                     if (!vertexInfoCache.TryGetValue(endPnt, out ifcVertexIdx))
                     {
                        IList<EdgeEndPoint> allEndPnts = SolidUtils.FindAllEdgeEndPointsAtVertex(endPnt);
                        if (allEndPnts.Count == 0)
                           return (null, 0);

                        // Use the average of all positions for the vertex location
                        XYZ vertexPosition = XYZ.Zero;
                        foreach (EdgeEndPoint adjacentEndPnt in allEndPnts)
                           vertexPosition += adjacentEndPnt.Evaluate();
                        vertexPosition /= allEndPnts.Count;

                        vertexPositions.Add(vertexPosition);

                        // IFC indices start from 1, so new vertex index is taken as vertices.Count instead of vertices.Count - 1
                        ifcVertexIdx = vertexPositions.Count;
                        foreach (EdgeEndPoint adjacentEndPnt in allEndPnts)
                           vertexInfoCache.Add(adjacentEndPnt, ifcVertexIdx);
                     }

                     indexedLoop.Add(ifcVertexIdx);
                  }
                  indexedLoops.Add(indexedLoop);

                  // The loop with the largest area will be considered the outer one.
                  // Typically SplitVolumes should be called before this function, so faces shouldn't have multiple outer loops.
                  if (face.EdgeLoops.Size > 1)
                  {
                     List<XYZ> loopOutline = indexedLoop.Select(x => vertexPositions[x - 1]).ToList();
                     double currArea = Math.Abs(GeometryUtil.ComputePolygonalLoopArea(loopOutline, face.FaceNormal, loopOutline[0]));
                     if (currArea > maxArea)
                     {
                        outerEdgeLoopIndex = indexedLoops.Count - 1;
                        maxArea = currArea;
                     }
                  }
               }

               if (indexedLoops.Count > 0)
                  loopsCache.Add(new Tuple<IList<IList<int>>, int>(indexedLoops, outerEdgeLoopIndex));
               else
                  return (null, 0);
            }
         }
         catch
         {
            // If anything unexpected handles the caller should move on to export the body as a tessellation
            return (null, 0);
         }

         // Create faces
         foreach (Tuple<IList<IList<int>>, int> loops in loopsCache)
         {
            if (loops.Item1.Count > 1)
               ifcFaceHandles.Add(IFCInstanceExporter.CreateIndexedPolygonalFaceWithVoids(file, loops.Item1[loops.Item2],
                  loops.Item1.Except(new List<IList<int>>() { loops.Item1[loops.Item2] }).ToList()));
            else if (loops.Item1.Count == 1)
               ifcFaceHandles.Add(IFCInstanceExporter.CreateIndexedPolygonalFace(file, loops.Item1[0]));
         }

         // Convert vertices to IFC representation
         List<IList<double>> vertexCoords = new List<IList<double>>();
         foreach (XYZ vertex in vertexPositions)
         {
            XYZ vertexScaled = TransformAndScalePoint(exporterIFC, vertex, trfToUse);
            vertexCoords.Add(new List<double>() { vertexScaled.X, vertexScaled.Y, vertexScaled.Z });
         }

         int numFaces = ifcFaceHandles.Count;
         if (numFaces == 0 || vertexCoords.Count == 0)
            return (null, 0);

         IFCAnyHandle coordinatesHnd = IFCInstanceExporter.CreateCartesianPointList3D(file, vertexCoords);
         IFCAnyHandle polygonalFaceSet = IFCInstanceExporter.CreatePolygonalFaceSet(file, coordinatesHnd, isClosed, ifcFaceHandles, null);
         return (polygonalFaceSet, numFaces);
      }

      private static void AddStyleToFaceSet(ExporterIFC exporterIFC, IFCFile file,
         Document document, BodyExporterOptions options, IFCAnyHandle polygonalFaceSet, 
         IFCAnyHandle ifcColourRgbList, double opacity, ElementId matId, int numFaces)
      {
         if (!IFCAnyHandleUtil.IsNullOrHasNoValue(ifcColourRgbList))
         {
            IList<int> colourIndex = Enumerable.Repeat(1, numFaces).ToList();
            IFCInstanceExporter.CreateIndexedColourMap(file, polygonalFaceSet, opacity, ifcColourRgbList, colourIndex);
         }
         CreateSurfaceStyleForRepItem(exporterIFC, document, options.CreatingVoid, polygonalFaceSet, matId);
      }

      /// <summary>
      /// Export Planar Solid geometry as IfcPolygonalFaceSet (IFC4-Add2)
      /// </summary>
      /// <param name="exporterIFC">the exporterIFC</param>
      /// <param name="element">the element</param>
      /// <param name="options">exporter options</param>
      /// <param name="geomObject">the geometry object of the element</param>
      /// <returns>a handle to the created IFCPolygonalFaceSet</returns>
      public static IList<IFCAnyHandle> ExportBodyAsPolygonalFaceSet(ExporterIFC exporterIFC, Element element, BodyExporterOptions options,
         GeometryObject geomObject, Transform trfToUse = null)
      {
         IFCFile file = exporterIFC.GetFile();
         
         Document document = element.Document;
         List<IFCAnyHandle> polygonalFaceSetList = new List<IFCAnyHandle>();

         // If the geomObject is GeometryELement or GeometryInstance, we need to collect their primitive Solid and Mesh first
         IList<GeometryObject> geomObjectPrimitives = GetGeometriesFromGeometryElement(
            exporterIFC, document, geomObject, false, out bool allNotToBeExported);

         // At this point all collected geometry will only contains Solid and/or Mesh
         foreach (GeometryObject geom in geomObjectPrimitives)
         {
            try
            {
               IList<IFCAnyHandle> polygonalFaceSets = new List<IFCAnyHandle>();

               IList<(TriangleMergeUtil, bool)> triangleComponents = 
                  new List<(TriangleMergeUtil, bool)>();
               IEnumerable<PlanarFace> planarFaceData = null;

               if (geom is Solid)
               {
                  Solid solid = geom as Solid;

                  planarFaceData = GetDataForExportPlanarSolidAsPolygonalFaceSet(solid);
                  if (planarFaceData == null)
                  {
                     TriangulatedSolidOrShell solidFacetation = GetOptimalTessellation(solid, options);

                     for (int ii = 0; ii < solidFacetation.ShellComponentCount; ++ii)
                     {
                        TriangulatedShellComponent component = solidFacetation.GetShellComponent(ii);
                        triangleComponents.Add((new TriangleMergeUtil(component), component.IsClosed));
                     }
                  }
               }
               else if (geom is Mesh)
               {
                  Mesh mesh = geom as Mesh;
                  triangleComponents.Add((new TriangleMergeUtil(mesh), mesh.IsClosed));
               }

               if (planarFaceData == null && triangleComponents.Count == 0)
               {
                  continue;
               }

               IFCAnyHandle ifcColourRgbList = GetBestColourAndOpacity(file, element, geom,
                  out double opacity, out ElementId matId);

               if (planarFaceData != null)
               {
                  (IFCAnyHandle polygonalFaceSet, int numFaces) = 
                     ExportPlanarSolidAsPolygonalFaceSet(exporterIFC, planarFaceData, trfToUse);
                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(polygonalFaceSet))
                  {
                     polygonalFaceSets.Add(polygonalFaceSet);
                     AddStyleToFaceSet(exporterIFC, file, document, options, polygonalFaceSet,
                        ifcColourRgbList, opacity, matId, numFaces);
                  }
               }

               foreach ((TriangleMergeUtil triMerge, bool isClosed) in triangleComponents)
               {
                  IList<IFCAnyHandle> ifcFaces = MergeAndCreateIfcFaces(file, triMerge);

                  IList<IList<double>> coordList = new List<IList<double>>();
                  foreach (XYZ vertex in triMerge.GetVertices())
                  {
                     XYZ vertexScaled = TransformAndScalePoint(exporterIFC, vertex, trfToUse);
                     coordList.Add(new List<double>() { vertexScaled.X, vertexScaled.Y, vertexScaled.Z });
                  }

                  IFCAnyHandle coordinatesHnd = IFCInstanceExporter.CreateCartesianPointList3D(file, coordList);
                  IFCAnyHandle polygonalFaceSet = IFCInstanceExporter.CreatePolygonalFaceSet(file, coordinatesHnd, isClosed, ifcFaces, null);
                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(polygonalFaceSet))
                  {
                     polygonalFaceSets.Add(polygonalFaceSet);
                     AddStyleToFaceSet(exporterIFC, file, document, options, polygonalFaceSet,
                        ifcColourRgbList, opacity, matId, ifcFaces.Count);
                  }
               }

               polygonalFaceSetList.AddRange(polygonalFaceSets);
            }
            catch
            {
               // Failed! Likely because either the tessellation or coplanar face merge failed.
               // Try to create from the FaceSet instead.
               polygonalFaceSetList.Clear();
               break;
            }
         }

         if (polygonalFaceSetList.Count == 0 && !allNotToBeExported)
         {
            // It is not from Solid, so we will use the faces to export. It works for Surface export too
            polygonalFaceSetList.AddRange(ExportSurfaceAsTriangulatedFaceSet(exporterIFC, element,
               options, geomObject, trfToUse));
         }

         return polygonalFaceSetList;
      }

      /// <summary>
      /// Tesselate the solid decreasing the number of facets if necessary.
      /// </summary>
      /// <param name="solid">The Solid</param>
      /// <param name="options">The body exported options.</param>
      /// <returns>Solid tessellation</returns>
      private static TriangulatedSolidOrShell GetOptimalTessellation(Solid solid, BodyExporterOptions options)
      {
         TriangulatedSolidOrShell solidFacetation = SolidUtils.TessellateSolidOrShell(solid, options.TessellationControls);

         SolidOrShellTessellationControls coarseTessellationControls = ExporterUtil.CopyTessellationControls(options.TessellationControls);
         BodyExporterOptions.SetDefaultCoarseTessellationControls(coarseTessellationControls);

         if (AreTessellationControlsEqual(coarseTessellationControls, options.TessellationControls))
         {
            // The tessellation controls are already at the coarsest level.
            return solidFacetation;
         }

         bool useCoarseTessellation = false;
         for (int ii = 0; ii < solidFacetation.ShellComponentCount; ii++)
         {
            TriangulatedShellComponent component = solidFacetation.GetShellComponent(ii);
            if (component.TriangleCount > MaximumAllowedFacets(options))
            {
               useCoarseTessellation = true;
               break;
            }
         }

         if (useCoarseTessellation)
            solidFacetation = SolidUtils.TessellateSolidOrShell(solid, coarseTessellationControls);

         return solidFacetation;
      }

      /// <summary>
      /// Collect Solid and/or Mesh from GeometryElement
      /// </summary>
      /// <param name="geomElement">the GeometryElement</param>
      /// <returns>list of Solid and/or Mesh</returns>
      private static List<GeometryObject> GetGeometryObjectListFromGeometryElement(Document doc, ExporterIFC exporterIFC, GeometryElement geomElement, out bool allNotToBeExported)
      {
         List<GeometryObject> geomObjectPrimitives = new List<GeometryObject>();
         SolidMeshGeometryInfo solidMeshCapsule = GeometryUtil.GetSplitSolidMeshGeometry(geomElement);
         int initialSolidMeshCount = solidMeshCapsule.GetSolids().Count + solidMeshCapsule.GetMeshes().Count;
         IList<Solid> solids = solidMeshCapsule.GetSolids();
         IList<Mesh> meshes = solidMeshCapsule.GetMeshes();
         geomObjectPrimitives = FamilyExporterUtil.RemoveInvisibleSolidsAndMeshes(doc, exporterIFC, ref solids, ref meshes);
         allNotToBeExported = initialSolidMeshCount > 0 && geomObjectPrimitives.Count == 0;

         return geomObjectPrimitives;
      }

      /// <summary>
      /// Merges faces in TriangleMergeUtil and exports them as a list of IFC faces
      /// </summary>
      /// <param name="file">the File</param>
      /// <param name="triMerge">TriangleMergeUtil instance with initialized geometry</param>
      /// <returns>List of IFC face handles</returns>
      private static IList<IFCAnyHandle> MergeAndCreateIfcFaces(IFCFile file, TriangleMergeUtil triMerge)
      {
         // TODO: Look at performance implications of large facetations.
         IList<IFCAnyHandle> faces = new List<IFCAnyHandle>();

         const bool tryToMerge = true;
         if (!triMerge.SimplifyAndMergeFaces(tryToMerge))
         {
            triMerge.Reset();
            triMerge.SimplifyAndMergeFaces(!tryToMerge);
         }


         for (int jj = 0; jj < triMerge.NoOfFaces; ++jj)
         {
            IFCAnyHandle faceHandle = null;
            IList<IList<int>> innerBoundariesOfFace = triMerge.IndexInnerBoundariesOfFaceAt(jj);
            bool faceWithHole = (innerBoundariesOfFace?.Count ?? 0) > 0;

            IList<int> outerBound = triMerge.IndexOuterboundOfFaceAt(jj).Select(x => x + 1).ToList();
            if (!faceWithHole)
            {
               faceHandle = IFCInstanceExporter.CreateIndexedPolygonalFace(file, outerBound);
            }
            else
            {
               IList<IList<int>> innerBounds = new List<IList<int>>();
               foreach (IList<int> inner in innerBoundariesOfFace)
                  innerBounds.Add(inner.Select(x => x + 1).ToList());

               faceHandle = IFCInstanceExporter.CreateIndexedPolygonalFaceWithVoids(file, outerBound, innerBounds);
            }

            faces.Add(faceHandle);
         }

         return faces;
      }

      private static int MaximumAllowedFacets(BodyExporterOptions options)
      {
         // We are going to limit the number of triangles to 25000 for Coarse tessellation, and 50000 otherwise.  
         // These are arbitrary numbers that should prevent the solid faceter from creating too many extra triangles to sew the surfaces.
         // We may evaluate this number over time.
         return (options.TessellationLevel == BodyExporterOptions.BodyTessellationLevel.Coarse) ? 25000 : 50000;
      }

      private static (IList<IList<double>>, IList<IList<int>>) GetMeshCoordinateInfo(
         ExporterIFC exporterIFC, Mesh mesh, Transform lcs, BodyExporterOptions options)
      {
         // Note that this function has two possible return values:
         // (coordList, coordIdx) == (empty, empty): Unhandled or empty mesh.
         // Anything else: valid data.
         IList<IList<double>> coordList = new List<IList<double>>();
         IList<IList<int>> coordIdx = new List<IList<int>>();

         int numberOfTriangles = mesh.NumTriangles;
         int numberOfVertices = mesh.Vertices.Count;

         // We are going to limit the number of triangles to prevent the solid faceter from creating too many extra triangles to sew the surfaces.
         if (numberOfTriangles == 0 || numberOfVertices == 0 || numberOfTriangles >= MaximumAllowedFacets(options))
         {
            return (coordList, coordIdx);
         }

         // create list of vertices first.
         foreach (XYZ vertex in mesh.Vertices)
         {
            XYZ vertexScaled = TransformAndScalePoint(exporterIFC, vertex, lcs);
            coordList.Add(new List<double>(3) { vertexScaled.X, vertexScaled.Y, vertexScaled.Z });
         }
         // Create the entity IfcCartesianPointList3D from the List of List<double> and assign it to attribute Coordinates of IfcTriangulatedFaceSet

         // Export all of the triangles
         for (int ii = 0; ii < numberOfTriangles; ii++)
         {
            MeshTriangle triangle = mesh.get_Triangle(ii);
            // IFC uses index that starts with 1 instead of 0 (following similar standard in X3D)
            coordIdx.Add(new List<int>(3)
                     {
                        (int)triangle.get_Index(0) + 1,
                        (int)triangle.get_Index(1) + 1,
                        (int)triangle.get_Index(2) + 1
                     });
         }

         return (coordList, coordIdx);
      }

      private static (IList<IList<double>>, IList<IList<int>>) GetSolidCoordinateInfo(
         ExporterIFC exporterIFC, Solid solid, Transform lcs, BodyExporterOptions options)
      {
         // Note that this function has three possible return values:
         // (coordList, coordIdx) == (null, null): Exception thrown because of invalid data.
         // (coordList, coordIdx) == (empty, empty): Unhandled or empty geometry.
         // Anything else: valid data.
         IList<IList<double>> coordList = new List<IList<double>>();
         IList<IList<int>> coordIdx = new List<IList<int>>();

         try
         {
            SolidOrShellTessellationControls tessellationControls = options.TessellationControls;
            TriangulatedSolidOrShell solidFacetation =
                SolidUtils.TessellateSolidOrShell(solid, tessellationControls);

            // Only handle one solid or shell.
            if (solidFacetation.ShellComponentCount != 1)
            {
               return (coordList, coordIdx);
            }

            TriangulatedShellComponent component = solidFacetation.GetShellComponent(0);
            int numberOfTriangles = component.TriangleCount;
            int numberOfVertices = component.VertexCount;

            // We are going to limit the number of triangles to prevent the solid faceter from creating too many extra triangles to sew the surfaces.
            if (numberOfTriangles == 0 || numberOfVertices == 0 || numberOfTriangles >= MaximumAllowedFacets(options))
            {
               return (coordList, coordIdx);
            }

            // create list of vertices first.
            for (int ii = 0; ii < numberOfVertices; ii++)
            {
               XYZ vertex = component.GetVertex(ii);
               XYZ vertexScaled = TransformAndScalePoint(exporterIFC, vertex, lcs);
               coordList.Add(new List<double>(3) { vertexScaled.X, vertexScaled.Y, vertexScaled.Z });
            }
            // Create the entity IfcCartesianPointList3D from the List of List<double> and assign it to attribute Coordinates of IfcTriangulatedFaceSet

            // Export all of the triangles
            for (int ii = 0; ii < numberOfTriangles; ii++)
            {
               TriangleInShellComponent triangle = component.GetTriangle(ii);
               // IFC uses index that starts with 1 instead of 0 (following similar standard in X3D)
               coordIdx.Add(new List<int>(3) { triangle.VertexIndex0 + 1, triangle.VertexIndex1 + 1, triangle.VertexIndex2 + 1 });
            }
         }
         catch
         {
            return (null, null);
         }

         return (coordList, coordIdx);
      }

      private static (IList<IList<double>>, IList<IList<int>>) GetGeometryCoordinateInfo(
         ExporterIFC exporterIFC, GeometryObject geom, Transform lcs, BodyExporterOptions options)
      {
         if (geom is Solid)
         {
            return GetSolidCoordinateInfo(exporterIFC, geom as Solid, lcs, options);
         }
         else if (geom is Mesh)
         {
            return GetMeshCoordinateInfo(exporterIFC, geom as Mesh, lcs, options);
         }

         // Return an empty list, to signify no error, just ignored.  Really shouldn't get here.
         return (new List<IList<double>>(), new List<IList<int>>());
      }

      /// <summary>
      /// Export Geometry in IFC4 Triangulated tessellation
      /// </summary>
      /// <param name="exporterIFC">the exporter</param>
      /// <param name="element">the element</param>
      /// <param name="options">the options</param>
      /// <param name="geomObject">geometry objects</param>
      /// <returns>returns a handle</returns>
      public static IList<IFCAnyHandle> ExportBodyAsTriangulatedFaceSet(ExporterIFC exporterIFC,
         Element element, BodyExporterOptions options, GeometryObject geomObject, Transform lcs = null)
      {
         IFCFile file = exporterIFC.GetFile();
         Document document = element.Document;

         List<IFCAnyHandle> triangulatedBodyList = new List<IFCAnyHandle>();

         List<int> colourIndex = new List<int>();

         // We need to collect all Solids and Meshes from the GeometryObject if it is of types GeometryElement or GeometryInstance
         // If the geomObject is GeometryELement or GeometryInstance, we need to collect their primitive Solid and Mesh first
         IList<GeometryObject> geomObjectPrimitives = GetGeometriesFromGeometryElement(
            exporterIFC, document, geomObject, true, out bool allNotToBeExported);

         // At this point the collection will only contains Solids and/or Meshes.
         // Loop through each of them.
         // Note that we will collect all of the coordList and coordIdx first since if any fail,
         // we will fall back to ExportSurfaceAsTriangulatedFaceSet.

         IList<(GeometryObject, IList<IList<double>>, IList<IList<int>>)> coordListsAndIndices = 
            new List<(GeometryObject, IList<IList<double>>, IList<IList<int>>)>();
         foreach (GeometryObject geom in geomObjectPrimitives)
         {
            IList<IList<double>> coordList = new List<IList<double>>();
            IList<IList<int>> coordIdx = new List<IList<int>>();
            (coordList, coordIdx) = GetGeometryCoordinateInfo(exporterIFC, geom, lcs, options);

            // Failed; go to fallback.
            if (coordList == null || coordIdx == null)
            {
               coordListsAndIndices.Clear();
               break;
            }

            if (coordList.Count > 0 && coordIdx.Count > 0)
            {
               coordListsAndIndices.Add((geom, coordList, coordIdx));
            }
         }

         foreach ((GeometryObject geom, IList<IList<double>> coordList, IList<IList<int>> coordIdx) in coordListsAndIndices)
         {
            IFCAnyHandle coordPointLists = IFCAnyHandleUtil.CreateInstance(file, IFCEntityType.IfcCartesianPointList3D);
            IFCAnyHandleUtil.SetAttribute(coordPointLists, "CoordList", coordList, 1, null, 3, 3);

            IFCAnyHandle triangulatedBody = IFCAnyHandleUtil.CreateInstance(file, IFCEntityType.IfcTriangulatedFaceSet);
            IFCAnyHandleUtil.SetAttribute(triangulatedBody, "Coordinates", coordPointLists);
            IFCAnyHandleUtil.SetAttribute(triangulatedBody, "CoordIndex", coordIdx, 1, null, 3, 3);

            triangulatedBodyList.Add(triangulatedBody);

            IFCAnyHandle ifcColourRgbList = GetBestColourAndOpacity(file, element,
               geom, out double opacity, out ElementId matId);

            // Currently each face will refer to just a single color in ColourRgbList
            colourIndex.AddRange(Enumerable.Repeat(1, coordIdx.Count));
            if (!IFCAnyHandleUtil.IsNullOrHasNoValue(ifcColourRgbList) &&
               !IFCAnyHandleUtil.IsNullOrHasNoValue(triangulatedBody))
            {
               IFCInstanceExporter.CreateIndexedColourMap(file, triangulatedBody, opacity, ifcColourRgbList, colourIndex);
            }

            if (matId != ElementId.InvalidElementId)
            {
               CreateSurfaceStyleForRepItem(exporterIFC, document, options.CreatingVoid,
                  triangulatedBody, matId);
            }
         }

         if (triangulatedBodyList.Count == 0 && !allNotToBeExported)
         {
            triangulatedBodyList.AddRange(ExportSurfaceAsTriangulatedFaceSet(exporterIFC, element,
               options, geomObject, lcs));
         }
         
         return triangulatedBodyList;
      }

      /// <summary>
      /// Export Geometry in IFC4 tessellation. Internally will decide to try to export as an IfcPolygonalFaceSet only when the Solid is all plannar
      /// </summary>
      /// <param name="exporterIFC">the exporter</param>
      /// <param name="element">the element</param>
      /// <param name="options">the options</param>
      /// <param name="geomObject">geometry objects</param>
      /// <returns>returns a handle</returns>
      public static IList<IFCAnyHandle> ExportBodyAsTessellatedFaceSet(ExporterIFC exporterIFC, Element element, BodyExporterOptions options,
         GeometryObject geomObject, Transform lcs = null)
      {
         IList<IFCAnyHandle> tessellatedBodyList = null;

         if (!ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4 && !ExporterCacheManager.ExportOptionsCache.UseOnlyTriangulation)
         {
            tessellatedBodyList = ExportBodyAsPolygonalFaceSet(exporterIFC, element, options, geomObject, lcs);
         }
         else
         {
            tessellatedBodyList = ExportBodyAsTriangulatedFaceSet(exporterIFC, element, options, geomObject, lcs);
         }

         return tessellatedBodyList;
      }

      private static (Color, double, ElementId) GetBestColourAndOpacity(Element element,
         GeometryObject geometryObject)
      {
         ElementId bestMaterialId = GetBestMaterialIdFromGeometryOrParameter(geometryObject, element);

         Color exportColor = null;
         Material matElem = (bestMaterialId != null && bestMaterialId != ElementId.InvalidElementId) ?
            element.Document.GetElement(bestMaterialId) as Material : null;
         if (matElem == null)
         {
            // If the material for the element is null, don't guess a color; null is OK.
            matElem = CategoryUtil.GetSafeCategory(element)?.Material;
            exportColor = matElem?.Color;
         }
         else
         {
            exportColor = CategoryUtil.GetSafeColor(matElem.Color);
         }

         double opacity = (double)(100 - (matElem?.Transparency ?? 0)) / 100;

         // For now we will only support a single color for the tessellation since there is no 
         // good way to associate the face and the color.
         return (exportColor, opacity, bestMaterialId);
      }

      private static IFCAnyHandle GetBestColourAndOpacity(IFCFile file, Element element, 
         GeometryObject geometryObject, out double opacity, out ElementId bestMaterialId)
      {
         Color exportColor;
         (exportColor, opacity, bestMaterialId) = GetBestColourAndOpacity(element, geometryObject);

         return (exportColor == null) ? null : ColourRgbListFromColor(file, exportColor);
      }

      /// <summary>
      /// Return a list of triangulated face sets from the geometry.
      /// </summary>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      /// <param name="element">The element.</param>
      /// <param name="options">The body exporter options.</param>
      /// <param name="geomObject">The geometry object.</param>
      /// <returns>Returns a list of handles.</returns>
      private static IList<IFCAnyHandle> ExportSurfaceAsTriangulatedFaceSet(
         ExporterIFC exporterIFC, Element element, BodyExporterOptions options,
         GeometryObject geomObject, Transform trfToUse = null)
      {
         IFCFile file = exporterIFC.GetFile();
         IList<IFCAnyHandle> listOfIndexedTriangles = new List<IFCAnyHandle>();

         List<(List<List<XYZ>>, Color, double, ElementId)> triangleLists = 
            new List<(List<List<XYZ>>, Color, double, ElementId)>();

         if (geomObject is GeometryElement)
         {
            // There is also a possibility that the geomObject is an GeometryElement thaat is a
            // collection of GeometryObjects. Go through the collection and get the Meshes, Solids,
            // and Faces.  We will skip everything else.

            // NOTE: We might have some "duplicate" colors in the list below.  For now, we will
            // allow that, since it is really a very minor optimization to compact the colors.
            foreach (GeometryObject geom in (geomObject as GeometryElement))
            {
               if (geom is Solid)
               {
                  triangleLists.AddRange(GetTriangleListsFromSolid(exporterIFC, element, geom, options, trfToUse));
               }
               else if (geom is Mesh)
               {
                  triangleLists.Add(GetTriangleListFromMesh(exporterIFC, element, geom, trfToUse, null));
               }
               else if (geom is Face)
               {
                  Mesh faceMesh = (geom as Face).Triangulate();
                  triangleLists.Add(GetTriangleListFromMesh(exporterIFC, element, faceMesh, trfToUse, geom));
               }
               else
               {
                  continue;
               }
            }
         }
         else
         {
            if (geomObject is Solid)
            {
               triangleLists.AddRange(GetTriangleListsFromSolid(exporterIFC, element, geomObject, options, trfToUse));
            }
            else if (geomObject is Mesh)
            {
               triangleLists.Add(GetTriangleListFromMesh(exporterIFC, element, geomObject, trfToUse, null));
            }
            else
            {
               return listOfIndexedTriangles;
            }
         }

         Document document = element?.Document;
         foreach ((List<List<XYZ>> triangleList, Color color, double opacity, 
            ElementId matId) in triangleLists)
         {
            if (triangleList.Count == 0)
            {
               continue;
            }

            IFCAnyHandle indexedTriangles = GeometryUtil.GetIndexedTriangles(file, triangleList);

            IFCAnyHandle ifcColourRgbList = ColourRgbListFromColor(file, color);

            if (!IFCAnyHandleUtil.IsNullOrHasNoValue(indexedTriangles))
            {
               List<int> colourIndex = Enumerable.Repeat(1, triangleList.Count).ToList();

               listOfIndexedTriangles.Add(indexedTriangles);
               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(ifcColourRgbList))
               {
                  IFCInstanceExporter.CreateIndexedColourMap(file, indexedTriangles, opacity, ifcColourRgbList, colourIndex);
               }

               if (matId != ElementId.InvalidElementId)
               {
                  CreateSurfaceStyleForRepItem(exporterIFC, document, options.CreatingVoid,
                     indexedTriangles, matId);
               }
            }
         }

         return listOfIndexedTriangles;
      }

      private static bool AreTessellationControlsEqual(SolidOrShellTessellationControls first, SolidOrShellTessellationControls second)
      {
         if (first.UseLevelOfDetail() != second.UseLevelOfDetail())
            return false;

         if (first.UseLevelOfDetail() && !MathUtil.IsAlmostEqual(first.LevelOfDetail, second.LevelOfDetail))
            return false;

         if (!MathUtil.IsAlmostEqual(first.Accuracy, second.Accuracy))
            return false;

         if (!MathUtil.IsAlmostEqual(first.MinAngleInTriangle, second.MinAngleInTriangle))
            return false;

         if (!MathUtil.IsAlmostEqual(first.MinExternalAngleBetweenTriangles, second.MinExternalAngleBetweenTriangles))
            return false;

         return true;
      }

      private static bool ExportBodyAsSolid(ExporterIFC exporterIFC, Element element, BodyExporterOptions options,
          IList<FaceSetInfo> currentFaceHashSetList, GeometryObject geomObject, Transform lcs)
      {
         IFCFile file = exporterIFC.GetFile();
         Document document = element.Document;
         if (!(geomObject is Solid))
            return false;

         Solid solid = geomObject as Solid;
         if (ExportPlanarBodyIfPossible(exporterIFC, solid, currentFaceHashSetList, lcs))
            return true;

         SolidOrShellTessellationControls tessellationControlsOriginal = options.TessellationControls;
         SolidOrShellTessellationControls tessellationControls = ExporterUtil.GetTessellationControl(element, tessellationControlsOriginal);

         TriangulatedSolidOrShell solidFacetation = null;
         TriangulatedShellComponent component = null;
         int numberOfTriangles = 0;
         int numberOfVertices = 0;

         // We will make (up to) 3 attempts.  
         // 1. First we will use tessellationControls.
         // 2. Try tessellationControlsOriginal, but only if they are different.
         // 3. Try a coarse tessellation, but only if the original wasn't coarse.
         bool useSolidTessellation = false;
         const int finalPass = 2;
         for (int pass = 0; pass <= finalPass; pass++)
         {
            SolidOrShellTessellationControls tessellationControlsToUse = null;

            switch (pass)
            {
               case 0:
                  tessellationControlsToUse = tessellationControls;
                  break;
               case 1:
                  if (!AreTessellationControlsEqual(tessellationControls, tessellationControlsOriginal))
                     tessellationControlsToUse = tessellationControlsOriginal;
                  break;
               case 2:
                  SolidOrShellTessellationControls coarseTessellationControls =
                     ExporterUtil.CopyTessellationControls(tessellationControlsOriginal);
                  BodyExporterOptions.SetDefaultCoarseTessellationControls(coarseTessellationControls);
                  if (!AreTessellationControlsEqual(coarseTessellationControls, tessellationControlsOriginal))
                     tessellationControlsToUse = coarseTessellationControls;
                  break;
            }

            if (tessellationControlsToUse == null)
               continue;

            try
            {
               solidFacetation = SolidUtils.TessellateSolidOrShell(solid, tessellationControlsToUse);
               // Only handle one solid or shell.
               if (solidFacetation == null || solidFacetation.ShellComponentCount != 1)
                  return false;

               component = solidFacetation.GetShellComponent(0);
               numberOfTriangles = component.TriangleCount;
               numberOfVertices = component.VertexCount;

               if ((numberOfTriangles > 0 && numberOfVertices > 0) && (numberOfTriangles < MaximumAllowedFacets(options)))
               {
                  useSolidTessellation = true;
                  break;
               }
            }
            catch (Exception ex)
            {
               string errMsg = String.Format("Pass {0}: TessellateSolidOrShell failed in IFC export for reason: \"{1}\" in element \"{2}\" with id {3}", pass, ex.Message, element.Name, element.Id);
               document.Application.WriteJournalComment(errMsg, false/*timestamp*/);

               // If the facetation failed, but we can try again, lets do so.
               if (pass == finalPass)
               {
                  return false;
               }
               continue;
            }
         }

         // We are going to limit the number of triangles to prevent the solid faceter from creating 
         // too many extra triangles to sew the surfaces.
         if (!useSolidTessellation)
            return false;

         IList<IFCAnyHandle> vertexHandles = new List<IFCAnyHandle>();
         HashSet<IFCAnyHandle> currentFaceSet = new HashSet<IFCAnyHandle>();
         bool canExportAsClosedShell = true;

         if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView || ExporterCacheManager.ExportOptionsCache.ExportAs4General)
         {
            List<List<double>> coordList = new List<List<double>>();

            // create list of vertices first.
            for (int ii = 0; ii < numberOfVertices; ii++)
            {
               XYZ vertex = component.GetVertex(ii);
               XYZ vertexScaled = TransformAndScalePoint(exporterIFC, vertex, lcs);
               coordList.Add(new List<double>(3) { vertexScaled.X, vertexScaled.Y, vertexScaled.Z });
            }
         }
         else
         {
            double vertexTolerance = ExporterCacheManager.LengthPrecision;
            IFCXYZFuzzyComparer ifcXYZFuzzyComparer = new IFCXYZFuzzyComparer(vertexTolerance);

            SortedDictionary<XYZ, IFCAnyHandle> createdVertices = new SortedDictionary<XYZ, IFCAnyHandle>(ifcXYZFuzzyComparer);

            // create list of vertices first.
            // Note that because we combine duplicate points, it is possible that a facet will have duplicate points.
            // We will need to remove those later.
            for (int ii = 0; ii < numberOfVertices; ii++)
            {
               IFCAnyHandle vertexHandle;
               XYZ vertex = component.GetVertex(ii);
               if (!createdVertices.TryGetValue(vertex, out vertexHandle))
               {
                  XYZ vertexScaled = TransformAndScalePoint(exporterIFC, vertex, lcs);
                  vertexHandle = ExporterUtil.CreateCartesianPoint(file, vertexScaled);
                  createdVertices[vertex] = vertexHandle;
               }
               vertexHandles.Add(vertexHandle);
            }

            if (!ExportPlanarFacetsIfPossible(file, component, vertexHandles, currentFaceSet, out canExportAsClosedShell))
            {
               HashSet<IList<IList<int>>> faceSetIndices = new();
               // Export all of the triangles instead.
               for (int ii = 0; ii < numberOfTriangles; ii++)
               {
                  TriangleInShellComponent triangle = component.GetTriangle(ii);

                  IFCAnyHandle vertex0 = vertexHandles[triangle.VertexIndex0];
                  IFCAnyHandle vertex1 = vertexHandles[triangle.VertexIndex1];
                  IFCAnyHandle vertex2 = vertexHandles[triangle.VertexIndex2];

                  List<IFCAnyHandle> vertices = new(3) { vertex0, vertex1, vertex2 };

                  IFCAnyHandle face = CreateFaceFromVertexList(file, vertices);
                  currentFaceSet.AddIfNotNull(face);

                  List<IList<int>> planarFaceIndices = new();
                  List<int> edgeLoopIndices = new(3) { vertex0.Id, vertex1.Id, vertex2.Id };

                  planarFaceIndices.Add(edgeLoopIndices);
                  faceSetIndices.Add(planarFaceIndices);
               }

               // Typically Solids in Revit are fully enclosed volumes,
               // but a shell or partially bounded volume can also be encountered
               canExportAsClosedShell = CanCreateClosedShell(faceSetIndices);
            }
         }

         currentFaceHashSetList.Add(new FaceSetInfo(currentFaceSet, canExportAsClosedShell));

         // Call GC.KeepAlive(solidFacetation) at this point to maintain a reference to solidFacetation
         // and prevent the object deletion by the garbage collector after try-catch block.
         GC.KeepAlive(solidFacetation);

         return true;
      }


      private static BodyData ExportBodyAsBRep(ExporterIFC exporterIFC, IList<GeometryObject> splitGeometryList,
          IList<KeyValuePair<int, SimpleSweptSolidAnalyzer>> exportAsBRep, IList<IFCAnyHandle> bodyItems,
          Element element, ElementId categoryId, ElementId overrideMaterialId, IFCAnyHandle contextOfItems, double eps,
          BodyExporterOptions options, BodyData bodyDataIn, bool instanceGeometry = true)
      {
         bool exportAsBReps = true;
         bool hasTriangulatedGeometry = false;
         bool hasAdvancedBrepGeometry = false;
         IFCFile file = exporterIFC.GetFile();
         Document document = element.Document;

         // Can't resetMaterials if we already have partially populated our body items with extrusions (canExportSolidModelRep)
         int numExtrusions = bodyItems.Count;
         bool resetMaterials = (numExtrusions == 0);
         IList<FaceSetInfo> currentFaceHashSetList = new List<FaceSetInfo>();
         IList<int> startIndexForObject = new List<int>();

         BodyData bodyData = BodyData.Create(bodyDataIn, resetMaterials);

         IList<ElementId> materialIds = new List<ElementId>();

         bool isCoarse = (options.TessellationLevel == BodyExporterOptions.BodyTessellationLevel.Coarse);

         int numBRepsToExport = exportAsBRep.Count;
         bool selectiveBRepExport = (numBRepsToExport > 0);
         int numGeoms = selectiveBRepExport ? numBRepsToExport : splitGeometryList.Count;

         bool elementIsSpatial = element is SpatialElement;
         bool canExportAsAdvancedGeometry = ExporterCacheManager.ExportOptionsCache.ExportAs4DesignTransferView;
         bool canExportAsTessellatedFaceSet = (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView || ExporterCacheManager.ExportOptionsCache.ExportAs4General) && !elementIsSpatial;

         // We will cycle through all of the geometries one at a time, doing the best export we can for each.
         for (int index = 0; index < numGeoms; index++)
         {
            int brepIndex = selectiveBRepExport ? exportAsBRep[index].Key : index;
            SimpleSweptSolidAnalyzer currAnalyzer = selectiveBRepExport ? exportAsBRep[index].Value : null;

            GeometryObject geomObject = splitGeometryList[brepIndex];

            // A simple test to see if the geometry is a valid solid.  This will save a lot of time in CanCreateClosedShell later.
            if (exportAsBReps && (geomObject is Solid))
            {
               try
               {
                  // We don't care what the value is here.  What we care about is whether or not it can be calculated.  If it can't be calculated,
                  // it is probably not a valid solid.
                  double volume = (geomObject as Solid).Volume;

                  // Current code should already prevent 0 volume solids from coming here, but may as well play it safe.
                  if (volume <= MathUtil.Eps())
                     exportAsBReps = false;
               }
               catch
               {
                  exportAsBReps = false;
               }
            }

            startIndexForObject.Add(currentFaceHashSetList.Count);

            ElementId materialId = SetBestMaterialIdInExporter(geomObject, element, overrideMaterialId, exporterIFC);
            materialIds.Add(materialId);
            bodyData.AddMaterial(materialId);

            bool alreadyExported = false;

            // First, see if this could be represented as a simple swept solid.
            if (exportAsBReps && (currAnalyzer != null))
            {
               SweptSolidExporter sweptSolidExporter = SweptSolidExporter.Create(exporterIFC, element, currAnalyzer, 
                  geomObject, GenerateAdditionalInfo.GenerateBody, isCoarse);
               HashSet<IFCAnyHandle> facetHnds = sweptSolidExporter?.Facets;
               if (facetHnds != null && facetHnds.Count != 0)
               {
                  // Current assumption: SweptSolidExporter produces valid solid geometry.
                  currentFaceHashSetList.Add(new FaceSetInfo(facetHnds, true));
                  alreadyExported = true;
                  GraphicsStyle style = document.GetElement(geomObject.GraphicsStyleId) as GraphicsStyle;
                  bodyData.AddRepresentationItemInfo(document, style, materialId, sweptSolidExporter.RepresentationItem);
               }
            }

            // Next, try to represent as an AdvancedBRep.
            if (!alreadyExported && canExportAsAdvancedGeometry)
            {
               IFCAnyHandle advancedBrepBodyItem = ExportBodyAsAdvancedBrep(exporterIFC, element, geomObject);
               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(advancedBrepBodyItem))
               {
                  bodyItems.Add(advancedBrepBodyItem);
                  alreadyExported = true;
                  hasAdvancedBrepGeometry = true;
                  CreateSurfaceStyleForRepItem(exporterIFC, document, options.CreatingVoid, advancedBrepBodyItem, materialId);
                  GraphicsStyle style = document.GetElement(geomObject.GraphicsStyleId) as GraphicsStyle;
                  bodyData.AddRepresentationItemInfo(document, style, materialId, advancedBrepBodyItem);
               }
            }

            // When element is FamilyInstance and geometry from symbol is used and the object is part of the Assembly, the transform needs to be Identity matrix
            Transform trfToUse = null;
            if (instanceGeometry)
               trfToUse = GeometryUtil.GetScaledTransform(exporterIFC);
            else if (!instanceGeometry && ExporterUtil.IsContainedInAssembly(element) && (element is FamilyInstance))
               trfToUse = Transform.Identity;

            // If we are using the Reference View, try a triangulated face set.
            // In theory, we could export a tessellated face set for geometry in the Design Transfer View that failed above.
            // However, FacetedBReps do hold more information (and aren't only triangles).
            if (!alreadyExported && canExportAsTessellatedFaceSet)
            {
               IList<IFCAnyHandle> triangulatedBodyItems = ExportBodyAsTessellatedFaceSet(exporterIFC, element, options, geomObject, trfToUse);
               if ((triangulatedBodyItems?.Count ?? 0) > 0)
               {
                  GraphicsStyle style = document.GetElement(geomObject.GraphicsStyleId) as GraphicsStyle;
                  foreach (IFCAnyHandle triangulatedBodyItem in triangulatedBodyItems)
                  {
                     bodyItems.Add(triangulatedBodyItem);
                     bodyData.AddRepresentationItemInfo(document, style, materialId, triangulatedBodyItem);
                  }
                  alreadyExported = true;
                  hasTriangulatedGeometry = true;
               }

               // We should log here that we couldn't export a geometry.  We aren't allowed to use the traditional methods below.
               if (!alreadyExported)
                  continue;
            }

            // If the above options do not generate any body, do the traditional step for Brep
            if (!alreadyExported && (exportAsBReps || isCoarse))
            {
               alreadyExported = ExportBodyAsSolid(exporterIFC, element, options, currentFaceHashSetList, geomObject, trfToUse);
            }

            // If all else fails, use the internal routine to go through the faces.  This will likely create a surface model.
            if (!alreadyExported)
            {
               IFCGeometryInfo faceListInfo = IFCGeometryInfo.CreateFaceGeometryInfo(eps, isCoarse);
               ExporterIFCUtils.CollectGeometryInfo(exporterIFC, faceListInfo, geomObject, XYZ.Zero, false);

               IList<ICollection<IFCAnyHandle>> faceSetList = faceListInfo.GetFaces();

               int numBReps = faceSetList.Count;
               if (numBReps == 0)
                  continue;

               foreach (ICollection<IFCAnyHandle> currentFaceSet in faceSetList)
               {
                  if (currentFaceSet.Count == 0)
                     continue;

                  // Default is true until we see that it is false.  This generally maintains
                  // current behavior, but probably needs to be improved.
                  bool canExportAsClosedShell = true;

                  if (exportAsBReps)
                  {
                     canExportAsClosedShell = (currentFaceSet.Count >= 4);
                     if (canExportAsClosedShell)
                     {
                        if ((geomObject is Mesh) && (numBReps == 1))
                        {
                           // use optimized version.
                           canExportAsClosedShell = CanCreateClosedShell(geomObject as Mesh);
                        }
                        else
                        {
                           canExportAsClosedShell = CanCreateClosedShell(currentFaceSet);
                        }
                     }

                     if (!canExportAsClosedShell)
                     {
                        exportAsBReps = false;

                        // We'll need to invalidate the extrusions we created and replace them with BReps.
                        if (selectiveBRepExport && (numGeoms != splitGeometryList.Count))
                        {
                           for (int fixIndex = 0; fixIndex < numExtrusions; fixIndex++)
                           {
                              RepresentationUtil.DeleteShapeRepresentation(bodyItems[0]);     // Use this instead of deleting directly because it may have entry in PresentationLayerSetCache
                              bodyItems.RemoveAt(0);
                           }
                           numExtrusions = 0;
                           numGeoms = splitGeometryList.Count;
                           int currBRepIndex = 0;
                           for (int fixIndex = 0; fixIndex < numGeoms; fixIndex++)
                           {
                              bool outOfRange = (currBRepIndex >= numBRepsToExport);
                              if (!outOfRange && (exportAsBRep[currBRepIndex].Key == fixIndex))
                              {
                                 currBRepIndex++;
                                 continue;
                              }
                              SimpleSweptSolidAnalyzer fixAnalyzer = outOfRange ? null : exportAsBRep[currBRepIndex].Value;
                              exportAsBRep.Add(new KeyValuePair<int, SimpleSweptSolidAnalyzer>(fixIndex, fixAnalyzer));
                           }
                           numBRepsToExport = exportAsBRep.Count;
                        }
                     }
                  }

                  currentFaceHashSetList.Add(new FaceSetInfo(new HashSet<IFCAnyHandle>(currentFaceSet), canExportAsClosedShell));
               }
            }
         }

         if (hasTriangulatedGeometry)
         {
            HashSet<IFCAnyHandle> bodyItemSet = new HashSet<IFCAnyHandle>();
            bodyItemSet.UnionWith(bodyItems);
            if (bodyItemSet.Count > 0)
            {
               bodyData.RepresentationHnd = RepresentationUtil.CreateTessellatedRep(exporterIFC, element, categoryId, contextOfItems, bodyItemSet, null);
               bodyData.ShapeRepresentationType = ShapeRepresentationType.Tessellation;
               //bodyData.RepresentationItems = bodyItemSet;
            }
         }
         else if (hasAdvancedBrepGeometry)
         {
            HashSet<IFCAnyHandle> bodyItemSet = new HashSet<IFCAnyHandle>();
            bodyItemSet.UnionWith(bodyItems);
            if (bodyItemSet.Count > 0)
            {
               bodyData.RepresentationHnd = RepresentationUtil.CreateAdvancedBRepRep(exporterIFC, element, categoryId, contextOfItems, bodyItemSet, null);
               bodyData.ShapeRepresentationType = ShapeRepresentationType.AdvancedBrep;
            }
         }
         else
         {
            int size = currentFaceHashSetList.Count;
            startIndexForObject.Add(size);  // end index for last object.

            for (int ii = 0; ii < size && exportAsBReps; ii++)
            {
               if (!currentFaceHashSetList[ii].IsClosed)
               {
                  exportAsBReps = false;
               }
            }

            // Export all spatial elements using IfcFacetedBRep, if possible.
            if (elementIsSpatial)
               exportAsBReps = true;

            IList<IFCAnyHandle> repMapItems = new List<IFCAnyHandle>();

            if (exportAsBReps)
            {
               int brepIndex = -1;
               ElementId currMatId = ElementId.InvalidElementId;
               GraphicsStyle currStyle = null;
               for (int ii = 0; ii < size; ii++)
               {
                  if (startIndexForObject[brepIndex + 1] == ii)
                  {
                     brepIndex++;
                     currMatId = materialIds[brepIndex];
                     currStyle = document.GetElement(splitGeometryList[brepIndex].GraphicsStyleId) as GraphicsStyle;
                  }

                  // If the face set is not closed for the spatial element, do not export it.
                  if (elementIsSpatial && !currentFaceHashSetList[ii].IsClosed)
                     continue;

                  HashSet<IFCAnyHandle> currentFaceHashSet = currentFaceHashSetList[ii].FaceSetHandles;
                  IFCAnyHandle faceOuter = IFCInstanceExporter.CreateClosedShell(file, currentFaceHashSet);
                  IFCAnyHandle brepHnd = RepresentationUtil.CreateFacetedBRep(exporterIFC,
                     document, options.CreatingVoid, faceOuter, currMatId);

                  if (bodyItems.AddIfNotNull(brepHnd))
                  {
                     bodyData.AddRepresentationItemInfo(document, currStyle, currMatId, brepHnd);
                  }
               }
            }
            else
            {
               IDictionary<ElementId, HashSet<IFCAnyHandle>> faceSets = new Dictionary<ElementId, HashSet<IFCAnyHandle>>();
               int matToUse = -1;
               for (int ii = 0; ii < size; ii++)
               {
                  HashSet<IFCAnyHandle> currentFaceHashSet = currentFaceHashSetList[ii].FaceSetHandles;
                  if (startIndexForObject[matToUse + 1] == ii)
                     matToUse++;

                  IFCAnyHandle faceSetHnd = IFCInstanceExporter.CreateConnectedFaceSet(file, currentFaceHashSet);
                  if (faceSets.TryGetValue(materialIds[matToUse], out HashSet<IFCAnyHandle> surfaceSet))
                  {
                     surfaceSet.Add(faceSetHnd);
                  }
                  else
                  {
                     surfaceSet = new HashSet<IFCAnyHandle>() { faceSetHnd };
                     faceSets[materialIds[matToUse]] = surfaceSet;
                  }
               }

               if (faceSets.Count > 0)
               {
                  foreach (KeyValuePair<ElementId, HashSet<IFCAnyHandle>> faceSet in faceSets)
                  {
                     IFCAnyHandle surfaceModel = IFCInstanceExporter.CreateFaceBasedSurfaceModel(file, faceSet.Value);
                     CreateSurfaceStyleForRepItem(exporterIFC, document, options.CreatingVoid, surfaceModel, faceSet.Key);

                     bodyItems.Add(surfaceModel);
                  }
               }
            }

            if (bodyItems.Count == 0)
               return bodyData;

            HashSet<IFCAnyHandle> bodyItemSet = new HashSet<IFCAnyHandle>();
            bodyItemSet.UnionWith(bodyItems);

            bodyData.ShapeRepresentationType = ShapeRepresentationType.Brep;
            if (exportAsBReps)
            {
               if (numExtrusions > 0)
                  bodyData.RepresentationHnd = RepresentationUtil.CreateSolidModelRep(exporterIFC, element, categoryId, contextOfItems, bodyItemSet);
               else
                  bodyData.RepresentationHnd = RepresentationUtil.CreateBRepRep(exporterIFC, element, categoryId, contextOfItems, bodyItemSet);
            }
            else
            {
               bodyData.RepresentationHnd = RepresentationUtil.CreateSurfaceRep(exporterIFC, element, categoryId, contextOfItems, bodyItemSet, false, null);
               bodyData.ShapeRepresentationType = ShapeRepresentationType.SurfaceModel;
            }
         }

         return bodyData;
      }

      private class SolidMetrics
      {
         int m_NumEdges;
         int m_NumFaces;
         double m_SurfaceArea;
         double m_Volume;

         public SolidMetrics(Solid solid)
         {
            NumEdges = solid.Edges.Size;
            NumFaces = solid.Faces.Size;
            SurfaceArea = solid.SurfaceArea;
            Volume = solid.Volume;
         }

         public int NumEdges
         {
            get { return m_NumEdges; }
            set { m_NumEdges = value; }
         }

         public int NumFaces
         {
            get { return m_NumFaces; }
            set { m_NumFaces = value; }
         }

         public double SurfaceArea
         {
            get { return m_SurfaceArea; }
            set { m_SurfaceArea = value; }
         }

         public double Volume
         {
            get { return m_Volume; }
            set { m_Volume = value; }
         }

         static public bool operator ==(SolidMetrics first, SolidMetrics second)
         {
            Object lhsObject = first;
            Object rhsObject = second;
            if (null == lhsObject)
            {
               if (null == rhsObject)
                  return true;
               return false;
            }
            if (null == rhsObject)
               return false;

            if (first.NumEdges != second.NumEdges)
               return false;

            if (first.NumFaces != second.NumFaces)
               return false;

            if (!MathUtil.IsAlmostEqual(first.SurfaceArea, second.SurfaceArea))
            {
               return false;
            }

            if (!MathUtil.IsAlmostEqual(first.Volume, second.Volume))
            {
               return false;
            }

            return true;
         }

         static public bool operator !=(SolidMetrics first, SolidMetrics second)
         {
            return !(first == second);
         }

         public override bool Equals(object obj)
         {
            if (obj == null)
               return false;

            SolidMetrics second = obj as SolidMetrics;
            return (this == second);
         }

         public override int GetHashCode()
         {
            double total = NumFaces + NumEdges + SurfaceArea + Volume;
            return (Math.Floor(total) * 100.0).GetHashCode();
         }
      }

      /// <summary>
      /// Exports list of geometries to IFC body representation.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="categoryId">The category id.</param>
      /// <param name="geometryListIn">The geometry list.</param>
      /// <param name="options">The settings for how to export the body.</param>
      /// <param name="exportBodyParams">The extrusion creation data.</param>
      /// <returns>The BodyData containing the handle, offset and material ids.</returns>
      public static BodyData ExportBody(ExporterIFC exporterIFC,
         Element element,
         ElementId categoryId,
         ElementId overrideMaterialId,
         IList<GeometryObject> geometryList,
         BodyExporterOptions options,
         IFCExportBodyParams exportBodyParams,
         GeometryObject potentialPathGeom = null,
         string profileName = null,
         bool instanceGeometry = true)
      {
         BodyData bodyData = new();
         if (geometryList.Count == 0)
            return bodyData;

         Document document = element.Document;
         
         bool tryToExportAsExtrusion = options.TryToExportAsExtrusion;
         bool canExportSolidModelRep = tryToExportAsExtrusion && ExporterCacheManager.ExportOptionsCache.CanExportSolidModelRep;

         // If we are exporting a coarse tessellation, or regardless if the level of detail isn't set to the highest level,
         // we will try to see if we can use an optimized BRep created from a swept solid.
         bool isCoarse = options.TessellationLevel == BodyExporterOptions.BodyTessellationLevel.Coarse;
         bool allowExportAsOptimizedBRep = (options.TessellationLevel == BodyExporterOptions.BodyTessellationLevel.Coarse ||
            ExporterCacheManager.ExportOptionsCache.LevelOfDetail < ExportOptionsCache.ExportTessellationLevel.High);
         bool allowAdvancedBReps = !ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4
                                    && !ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView
                                    && !ExporterCacheManager.ExportOptionsCache.ExportAs4General;

         // We will try to export as a swept solid if the option is set, and we are either exporting to a schema that allows it,
         // or we are using a coarse tessellation, in which case we will export the swept solid as an optimzed BRep.
         bool tryToExportAsSweptSolid = options.TryToExportAsSweptSolid && (allowAdvancedBReps || allowExportAsOptimizedBRep);

         // We will allow exporting swept solids as BReps or TriangulatedFaceSet if we are exporting to a schema before IFC4, or to a Reference View MVD, 
         // and we allow coarse representations.  In the future, we may allow more control here.
         // Note that we disable IFC4 because in IFC4, we will export it as a true swept solid instead, except for the Reference View MVD.
         bool tryToExportAsSweptSolidAsTessellation = tryToExportAsSweptSolid && allowExportAsOptimizedBRep && !allowAdvancedBReps;

         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle contextOfItems = ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Body);

         double eps = UnitUtil.ScaleLength(document.Application.VertexTolerance);

         bool allFaces = true;
         foreach (GeometryObject geomObject in geometryList)
         {
            if (!(geomObject is Face))
            {
               allFaces = false;
               break;
            }
         }

         IList<IFCAnyHandle> bodyItems = new List<IFCAnyHandle>();
         IList<ElementId> materialIdsForExtrusions = new List<ElementId>();

         // This is a list of geometries that can be exported using the coarse facetation of the SweptSolidExporter.
         IList<KeyValuePair<int, SimpleSweptSolidAnalyzer>> exportAsBRep = new List<KeyValuePair<int, SimpleSweptSolidAnalyzer>>();

         List<int> exportAsSweptSolid = [];
         List<int> exportAsExtrusion = [];

         bool hasExtrusions = false;
         bool hasSweptSolids = false;
         bool hasSweptSolidsAsBReps = false;
         ShapeRepresentationType hasRepresentationType = ShapeRepresentationType.Undefined;

         BoundingBoxXYZ bbox = GeometryUtil.GetBBoxOfGeometries(geometryList);
         XYZ unscaledTrfOrig = new();

         int numItems = geometryList.Count;
         bool tryExtrusionAnalyzer = tryToExportAsExtrusion && (options.ExtrusionLocalCoordinateSystem != null) && (numItems == 1) && (geometryList[0] is Solid);
         bool supportOffsetTransformForExtrusions = !(tryExtrusionAnalyzer || tryToExportAsSweptSolidAsTessellation);
         bool useOffsetTransformForExtrusions = options.AllowOffsetTransform && supportOffsetTransformForExtrusions && (exportBodyParams != null);

         MaterialAndProfile materialAndProfile = null;
         HashSet<FootPrintInfo> footprintInfoSet = new();
         Plane extrusionBasePlane = null;
         XYZ extrusionDirection = XYZ.BasisX;

         using (IFCTransaction tr = new(file))
         {
            // generate "bottom corner" of bbox; create new local placement if passed in.
            // need to transform, but not scale, this point to make it the new origin.
            using (TransformSetter transformSetter = TransformSetter.Create())
            {
               if (useOffsetTransformForExtrusions)
               {
                  bodyData.OffsetTransform = transformSetter.InitializeFromBoundingBox(exporterIFC, bbox, 
                     exportBodyParams, element.Location, instanceGeometry, out unscaledTrfOrig);
               }
               else
               {
                  bodyData.OffsetTransform = Transform.Identity;
               }

               // If we passed in an ExtrusionLocalCoordinateSystem, and we have 1 Solid, we will try to create an extrusion using the ExtrusionAnalyzer.
               // If we succeed, we will skip the rest of the routine, otherwise we will try with the backup extrusion method.
               // This doesn't yet create fallback information for solid models that are hybrid extrusions and BReps.
               if (tryToExportAsExtrusion)
               {
                  if (tryExtrusionAnalyzer)
                  {
                     using (IFCTransaction extrusionTransaction = new IFCTransaction(file))
                     {
                        if (exportBodyParams != null && exportBodyParams.PossibleExtrusionAxes == IFCExtrusionAxes.TryZ)
                        {
                           XYZ planeXVec = options.ExtrusionLocalCoordinateSystem.BasisX.Normalize();
                           XYZ planeYVec = options.ExtrusionLocalCoordinateSystem.BasisY.Normalize();

                           extrusionBasePlane = GeometryUtil.CreatePlaneByXYVectorsAtOrigin(planeXVec, planeYVec);
                           extrusionDirection = options.ExtrusionLocalCoordinateSystem.BasisZ;
                        }
                        else if (exportBodyParams != null && exportBodyParams.PossibleExtrusionAxes == IFCExtrusionAxes.TryY)
                        {
                           XYZ planeXVec = options.ExtrusionLocalCoordinateSystem.BasisX.Normalize();
                           XYZ planeYVec = options.ExtrusionLocalCoordinateSystem.BasisZ.Normalize();

                           extrusionBasePlane = GeometryUtil.CreatePlaneByXYVectorsAtOrigin(planeXVec, planeYVec);
                           extrusionDirection = options.ExtrusionLocalCoordinateSystem.BasisY;
                        }
                        else
                        {
                           XYZ planeXVec = options.ExtrusionLocalCoordinateSystem.BasisY.Normalize();
                           XYZ planeYVec = options.ExtrusionLocalCoordinateSystem.BasisZ.Normalize();

                           extrusionBasePlane = GeometryUtil.CreatePlaneByXYVectorsAtOrigin(planeXVec, planeYVec);
                           extrusionDirection = options.ExtrusionLocalCoordinateSystem.BasisX;
                        }

                        GenerateAdditionalInfo footprintOrProfile = GenerateAdditionalInfo.GenerateBody;
                        if (options.CollectFootprintHandle)
                           footprintOrProfile |= GenerateAdditionalInfo.GenerateFootprint;
                        if (options.CollectMaterialAndProfile)
                           footprintOrProfile |= GenerateAdditionalInfo.GenerateProfileDef;

                        ExtrusionExporter.ExtraClippingData extraClippingData = null;
                        // Why is this different from categoryId?
                        ElementId catId = CategoryUtil.GetSafeCategoryId(element);

                        HandleAndData extrusionData = ExtrusionExporter.CreateExtrusionWithClippingAndProperties(
                           exporterIFC, element, options.CreatingVoid, catId, geometryList[0] as Solid, 
                           extrusionBasePlane, options.ExtrusionLocalCoordinateSystem.Origin, extrusionDirection, 
                           null, out extraClippingData, addInfo: footprintOrProfile, profileName: profileName);
                        if (!extraClippingData.CompletelyClipped && !IFCAnyHandleUtil.IsNullOrHasNoValue(extrusionData.Handle))
                        {
                           // There are two valid cases here:
                           // 1. We actually created an extrusion.
                           // 2. We are in the Reference View, and we created a TriangulatedFaceSet.
                           if (extrusionData.BaseRepresentationItems != null && extrusionData.BaseRepresentationItems.Count == 1)
                           {
                              IList<ElementId> materialIds = extrusionData.MaterialIds;

                              // We skip setting and getting the material id from the exporter as unnecessary.
                              ElementId matIdFromGeom = (materialIds != null && materialIds.Count > 0) ? materialIds.First() : ElementId.InvalidElementId;
                              ElementId matId = (overrideMaterialId != ElementId.InvalidElementId) ? overrideMaterialId : matIdFromGeom;

                              materialIdsForExtrusions.Add(matId);
                              if (matId != ElementId.InvalidElementId)
                              {
                                 GraphicsStyle style = document.GetElement(geometryList[0].GraphicsStyleId) as GraphicsStyle;
                                 bodyData.AddRepresentationItemInfo(document, style, matId, extrusionData.BaseRepresentationItems[0]);
                              }
                              bodyData.RepresentationHnd = extrusionData.Handle;
                              bodyData.ShapeRepresentationType = extrusionData.ShapeRepresentationType;
                              bodyData.MaterialAndProfile = extrusionData.MaterialAndProfile;
                              bodyData.FootprintInfo = extrusionData.FootprintInfo;
                              HashSet<IFCAnyHandle> items = new HashSet<IFCAnyHandle>();
                              items.UnionWith(extrusionData.BaseRepresentationItems);

                              bodyItems.Add(extrusionData.BaseRepresentationItems[0]);

                              if (exportBodyParams != null && extrusionData.Data != null)
                              {
                                 exportBodyParams.Slope = extrusionData.Data.Slope;
                                 exportBodyParams.ScaledLength = extrusionData.Data.ScaledLength;
                                 exportBodyParams.ExtrusionDirection = extrusionData.Data.ExtrusionDirection;

                                 exportBodyParams.ScaledHeight = extrusionData.Data.ScaledHeight;
                                 exportBodyParams.ScaledWidth = extrusionData.Data.ScaledWidth;

                                 exportBodyParams.ScaledArea = extrusionData.Data.ScaledArea;
                                 exportBodyParams.ScaledInnerPerimeter = extrusionData.Data.ScaledInnerPerimeter;
                                 exportBodyParams.ScaledOuterPerimeter = extrusionData.Data.ScaledOuterPerimeter;
                              }

                              hasExtrusions = true;
                              if ((footprintOrProfile & GenerateAdditionalInfo.GenerateFootprint) != 0)
                                 footprintInfoSet.Add(extrusionData.FootprintInfo);
                              if ((footprintOrProfile & GenerateAdditionalInfo.GenerateProfileDef) != 0)
                                 materialAndProfile = extrusionData.MaterialAndProfile;

                              extrusionTransaction.Commit();
                           }
                        }

                        if (!hasExtrusions)
                           extrusionTransaction.RollBack();
                     }
                  }

                  // Only try if ExtrusionAnalyzer wasn't called, or failed.
                  if (!hasExtrusions)
                  {
                     // Check to see if we have Geometries or GFaces.
                     // We will have the specific all GFaces case and then the generic case.
                     IList<Face> faces = null;

                     if (allFaces)
                     {
                        faces = new List<Face>();
                        foreach (GeometryObject geometryObject in geometryList)
                        {
                           faces.Add(geometryObject as Face);
                        }
                     }

                     // Options used if we try to export extrusions.
                     IFCExtrusionAxes axesToExtrudeIn = exportBodyParams != null ? exportBodyParams.PossibleExtrusionAxes : IFCExtrusionAxes.TryDefault;
                     XYZ directionToExtrudeIn = XYZ.Zero;
                     if (exportBodyParams != null && exportBodyParams.HasCustomAxis)
                        directionToExtrudeIn = exportBodyParams.CustomAxis;

                     double lengthScale = UnitUtil.ScaleLengthForRevitAPI();
                     IFCExtrusionCalculatorOptions extrusionOptions =
                        new IFCExtrusionCalculatorOptions(exporterIFC, axesToExtrudeIn, directionToExtrudeIn, lengthScale);

                     int numExtrusionsToCreate = allFaces ? 1 : geometryList.Count;

                     IList<IList<IFCExtrusionData>> extrusionLists = new List<IList<IFCExtrusionData>>();
                     for (int ii = 0; ii < numExtrusionsToCreate; ii++)
                     {
                        IList<IFCExtrusionData> extrusionList = new List<IFCExtrusionData>();

                        if (tryToExportAsExtrusion)
                        {
                           if (allFaces)
                              extrusionList = IFCExtrusionCalculatorUtils.CalculateExtrusionData(extrusionOptions, faces);
                           else
                              extrusionList = IFCExtrusionCalculatorUtils.CalculateExtrusionData(extrusionOptions, geometryList[ii]);
                        }

                        if (extrusionList.Count == 0)
                        {
                           // If we are trying to create swept solids, we will keep going, but we won't try to create more extrusions unless we are also exporting a solid model.
                           if (tryToExportAsSweptSolid)
                           {
                              if (!canExportSolidModelRep)
                                 tryToExportAsExtrusion = false;
                              exportAsSweptSolid.Add(ii);
                           }
                           else if (!canExportSolidModelRep)
                           {
                              tryToExportAsExtrusion = false;
                              break;
                           }
                           else
                              exportAsBRep.Add(new KeyValuePair<int, SimpleSweptSolidAnalyzer>(ii, null));
                        }
                        else
                        {
                           extrusionLists.Add(extrusionList);
                           exportAsExtrusion.Add(ii);
                        }
                     }

                     int numCreatedExtrusions = extrusionLists.Count;
                     for (int ii = 0; ii < numCreatedExtrusions && tryToExportAsExtrusion; ii++)
                     {
                        int geomIndex = exportAsExtrusion[ii];

                        ElementId matId = SetBestMaterialIdInExporter(geometryList[geomIndex], element, overrideMaterialId, exporterIFC);

                        if (exportBodyParams != null && exportBodyParams.AreInnerRegionsOpenings)
                        {
                           IList<CurveLoop> curveLoops = extrusionLists[ii][0].GetLoops();
                           XYZ extrudedDirection = extrusionLists[ii][0].ExtrusionDirection;

                           int numLoops = curveLoops.Count;
                           for (int jj = numLoops - 1; jj > 0; jj--)
                           {
                              ExtrusionExporter.AddOpeningData(exportBodyParams, extrusionLists[ii][0], curveLoops[jj]);
                              extrusionLists[ii][0].RemoveLoopAt(jj);
                           }
                        }

                        bool exportedAsExtrusion = false;
                        IFCExtrusionBasis whichBasis = extrusionLists[ii][0].ExtrusionBasis;
                        if (whichBasis >= 0)
                        {
                           Transform lcs = Transform.Identity;
                           IFCAnyHandle extrusionHandle = ExtrusionExporter.CreateExtrudedSolidFromExtrusionData(exporterIFC,
                              element, extrusionLists[ii][0], out lcs, profileName: profileName);
                           if (bodyItems.AddIfNotNull(extrusionHandle))
                           {
                              materialIdsForExtrusions.Add(exporterIFC.GetMaterialIdForCurrentExportState());

                              IList<CurveLoop> curveLoops = extrusionLists[ii][0].GetLoops();
                              extrusionDirection = extrusionLists[ii][0].ExtrusionDirection;
                              if (options.CollectFootprintHandle)
                              {
                                 FootPrintInfo fInfo = new FootPrintInfo(curveLoops, lcs);
                                 footprintInfoSet.Add(fInfo);
                              }
                              if (options.CollectMaterialAndProfile)
                              {
                                 // Get the handle to the extrusion Swept Area needed for creation of IfcMaterialProfile
                                 IFCData extrArea = extrusionHandle.GetAttribute("SweptArea");
                                 if (materialAndProfile == null)
                                    materialAndProfile = new MaterialAndProfile();
                                 materialAndProfile.Add(exporterIFC.GetMaterialIdForCurrentExportState(), extrArea.AsInstance());
                              }

                              if (exportBodyParams != null)
                              {
                                 exportBodyParams.Slope = GeometryUtil.GetSimpleExtrusionSlope(extrusionDirection, whichBasis);
                                 exportBodyParams.ScaledLength = extrusionLists[ii][0].ScaledExtrusionLength;
                                 exportBodyParams.ExtrusionDirection = extrusionDirection;
                                 for (int kk = 1; kk < extrusionLists[ii].Count; kk++)
                                 {
                                    ExtrusionExporter.AddOpeningData(exportBodyParams, extrusionLists[ii][kk]);
                                 }

                                 double height = 0.0, width = 0.0;
                                 if (GeometryUtil.ComputeHeightWidthOfCurveLoop(curveLoops[0], exportBodyParams.PreferredWidthDirection, out height, out width))
                                 {
                                    exportBodyParams.ScaledHeight = UnitUtil.ScaleLength(height);
                                    exportBodyParams.ScaledWidth = UnitUtil.ScaleLength(width);
                                 }

                                 double area = ExporterIFCUtils.ComputeAreaOfCurveLoops(new[] { curveLoops[0] });
                                 if (area > 0.0)
                                 {
                                    exportBodyParams.ScaledArea = UnitUtil.ScaleArea(area);
                                 }

                                 double innerPerimeter = ExtrusionExporter.ComputeInnerPerimeterOfCurveLoops(curveLoops);
                                 double outerPerimeter = ExtrusionExporter.ComputeOuterPerimeterOfCurveLoops(curveLoops);
                                 if (innerPerimeter > 0.0)
                                    exportBodyParams.ScaledInnerPerimeter = UnitUtil.ScaleLength(innerPerimeter);
                                 if (outerPerimeter > 0.0)
                                    exportBodyParams.ScaledOuterPerimeter = UnitUtil.ScaleLength(outerPerimeter);
                              }
                              exportedAsExtrusion = true;
                              hasExtrusions = true;
                              GraphicsStyle style = document.GetElement(geometryList[geomIndex].GraphicsStyleId) as GraphicsStyle;

                              bodyData.AddRepresentationItemInfo(document, style, matId, extrusionHandle);
                           }
                        }

                        if (!exportedAsExtrusion)
                        {
                           if (tryToExportAsSweptSolid)
                              exportAsSweptSolid.Add(ii);
                           else if (!canExportSolidModelRep)
                           {
                              tryToExportAsExtrusion = false;
                              break;
                           }
                           else
                              exportAsBRep.Add(new KeyValuePair<int, SimpleSweptSolidAnalyzer>(ii, null));
                        }
                     }
                  }
               }

               if (tryToExportAsSweptSolid)
               {
                  int numCreatedSweptSolids = exportAsSweptSolid.Count;
                  for (int ii = 0; (ii < numCreatedSweptSolids) && tryToExportAsSweptSolid; ii++)
                  {
                     bool exported = false;
                     int geomIndex = exportAsSweptSolid[ii];
                     Solid solid = geometryList[geomIndex] as Solid;
                     SimpleSweptSolidAnalyzer simpleSweptSolidAnalyzer = null;
                     // TODO: allFaces to SweptSolid
                     if (solid != null)
                     {
                        // TODO: give normal hint below if we have an idea.
                        XYZ directrixPlaneNormal = (potentialPathGeom != null && potentialPathGeom is Arc) ? (potentialPathGeom as Arc).Normal : null;
                        if (directrixPlaneNormal == null)
                           directrixPlaneNormal = (potentialPathGeom != null && potentialPathGeom is Ellipse) ? (potentialPathGeom as Ellipse).Normal : null;
                        simpleSweptSolidAnalyzer = SweptSolidExporter.CanExportAsSweptSolid(exporterIFC, solid, directrixPlaneNormal, potentialPathGeom);

                        // If we are exporting as a BRep, we will keep the analyzer for later, if it isn't null.
                        if (simpleSweptSolidAnalyzer != null)
                        {
                           if (!tryToExportAsSweptSolidAsTessellation)
                           {
                              GenerateAdditionalInfo addInfo = GenerateAdditionalInfo.GenerateBody;   // Default
                              if (options.CollectFootprintHandle)
                                 addInfo |= GenerateAdditionalInfo.GenerateFootprint;

                              SweptSolidExporter sweptSolidExporter = SweptSolidExporter.Create(exporterIFC, element, 
                                 simpleSweptSolidAnalyzer, solid, addInfo, isCoarse);
                              IFCAnyHandle sweptHandle = sweptSolidExporter?.RepresentationItem;

                              if (bodyItems.AddIfNotNull(sweptHandle))
                              {
                                 ElementId matId = exporterIFC.GetMaterialIdForCurrentExportState();
                                 materialIdsForExtrusions.Add(matId);
                                 GraphicsStyle style = document.GetElement(solid.GraphicsStyleId) as GraphicsStyle;
                                 bodyData.AddRepresentationItemInfo(document, style, matId, sweptHandle);
                                 exported = true;
                                 hasRepresentationType = sweptSolidExporter.RepresentationType;

                                 // These are the only two valid cases for true sweep export: either an extrusion or a sweep.
                                 // We don't expect regular BReps or triangulated face sets here.
                                 if (sweptSolidExporter.isSpecificRepresentationType(ShapeRepresentationType.SweptSolid))
                                    hasExtrusions = true;
                                 else if (sweptSolidExporter.isSpecificRepresentationType(ShapeRepresentationType.AdvancedSweptSolid))
                                    hasSweptSolids = true;

                                 if (options.CollectFootprintHandle)
                                 {
                                    if (sweptSolidExporter.FootprintInfo != null)
                                       footprintInfoSet.Add(sweptSolidExporter.FootprintInfo);
                                 }
                                 if (options.CollectMaterialAndProfile)
                                 {
                                    // Get the handle to the extrusion Swept Area needed for creation of IfcMaterialProfile
                                    IFCData extrArea = sweptHandle.GetAttribute("SweptArea");
                                    if (materialAndProfile == null)
                                       materialAndProfile = new MaterialAndProfile();
                                    materialAndProfile.Add(exporterIFC.GetMaterialIdForCurrentExportState(), extrArea.AsInstance());
                                    materialAndProfile.PathCurve = simpleSweptSolidAnalyzer.PathCurve;
                                 }
                              }
                              else
                                 simpleSweptSolidAnalyzer = null;    // Didn't work for some reason.
                           }
                        }
                     }

                     if (!exported)
                     {
                        exportAsBRep.Add(new KeyValuePair<int, SimpleSweptSolidAnalyzer>(geomIndex, simpleSweptSolidAnalyzer));
                        hasSweptSolidsAsBReps |= (simpleSweptSolidAnalyzer != null);
                     }
                  }
               }

               bool exportSucceeded = (exportAsBRep.Count == 0) && (tryToExportAsExtrusion || tryToExportAsSweptSolid)
                  && (hasExtrusions || hasSweptSolids || hasRepresentationType != ShapeRepresentationType.Undefined);
               if (exportSucceeded || canExportSolidModelRep)
               {
                  int sz = bodyItems.Count();
                  for (int ii = 0; ii < sz; ii++)
                     CreateSurfaceStyleForRepItem(exporterIFC, document, options.CreatingVoid, bodyItems[ii], materialIdsForExtrusions[ii]);

                  if (exportSucceeded)
                  {
                     if (bodyData.RepresentationHnd == null)
                     {
                        HashSet<IFCAnyHandle> bodyItemSet = new HashSet<IFCAnyHandle>();
                        bodyItemSet.UnionWith(bodyItems);
                        if (hasExtrusions && !hasSweptSolids)
                        {
                           bodyData.RepresentationHnd =
                              RepresentationUtil.CreateSweptSolidRep(exporterIFC, element, categoryId, contextOfItems, bodyItemSet, bodyData.RepresentationHnd, exportBodyParams?.IFCCADLayerOverride);
                           bodyData.ShapeRepresentationType = ShapeRepresentationType.SweptSolid;
                           bodyData = SaveMaterialAndFootprintInfo(bodyData, materialAndProfile, footprintInfoSet, options.CollectFootprintHandle);
                        }
                        else if (hasSweptSolids && !hasExtrusions)
                        {
                           bodyData.RepresentationHnd =
                              RepresentationUtil.CreateAdvancedSweptSolidRep(exporterIFC, element, categoryId, contextOfItems, bodyItemSet, bodyData.RepresentationHnd);
                           bodyData.ShapeRepresentationType = ShapeRepresentationType.AdvancedSweptSolid;
                           bodyData = SaveMaterialAndFootprintInfo(bodyData, materialAndProfile, footprintInfoSet, options.CollectFootprintHandle);
                        }
                        else if (hasRepresentationType == ShapeRepresentationType.Tessellation)
                        {
                           bodyData.RepresentationHnd =
                              RepresentationUtil.CreateTessellatedRep(exporterIFC, element, categoryId, contextOfItems, bodyItemSet, bodyData.RepresentationHnd);
                           bodyData.ShapeRepresentationType = ShapeRepresentationType.Tessellation;
                        }
                        else
                        {
                           bodyData.RepresentationHnd =
                              RepresentationUtil.CreateSolidModelRep(exporterIFC, element, categoryId, contextOfItems, bodyItemSet);
                           bodyData.ShapeRepresentationType = ShapeRepresentationType.SolidModel;
                        }
                     }

                     // TODO: include BRep, CSG, Clipping

                     XYZ lpOrig = ((bodyData != null) && (bodyData.OffsetTransform != null)) ? UnitUtil.ScaleLength(bodyData.OffsetTransform.Origin) : new XYZ();
                     transformSetter.CreateLocalPlacementFromOffset(exporterIFC, bbox, exportBodyParams, lpOrig, unscaledTrfOrig);
                     tr.Commit();

                     return bodyData;
                  }
               }

               // If we are going to export a solid model, keep the created items.
               if (!canExportSolidModelRep)
                  tr.RollBack();
               else
                  tr.Commit();
            }

            // We couldn't export it as an extrusion; export as a solid, brep, or a surface model.
            if (!canExportSolidModelRep)
            {
               exportAsExtrusion.Clear();
               bodyItems.Clear();
               if (exportBodyParams != null)
                  exportBodyParams.ClearOpenings();
            }

            if (exportAsExtrusion.Count == 0)
            {
               // We used to clear exportAsBRep, but we need the SimpleSweptSolidAnalyzer information, so we will fill out the rest.
               int numGeoms = geometryList.Count;
               IList<KeyValuePair<int, SimpleSweptSolidAnalyzer>> newExportAsBRep = new List<KeyValuePair<int, SimpleSweptSolidAnalyzer>>(numGeoms);
               int exportAsBRepCount = exportAsBRep.Count;
               int currIndex = 0;
               for (int ii = 0; ii < numGeoms; ii++)
               {
                  if ((currIndex < exportAsBRepCount) && (ii == exportAsBRep[currIndex].Key))
                  {
                     newExportAsBRep.Add(exportAsBRep[currIndex]);
                     currIndex++;
                  }
                  else
                     newExportAsBRep.Add(new KeyValuePair<int, SimpleSweptSolidAnalyzer>(ii, null));
               }
               exportAsBRep = newExportAsBRep;
            }
         }

         // If we created some extrusions that we are using (e.g., creating a solid model), and we didn't use an offset transform for the extrusions, don't do it here either.
         bool supportOffsetTransformForBreps = !hasSweptSolidsAsBReps;
         bool disallowOffsetTransformForBreps = ((exportAsExtrusion.Count > 0) && !useOffsetTransformForExtrusions)
               || (element is Part && ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView);
         bool useOffsetTransformForBReps = options.AllowOffsetTransform && supportOffsetTransformForBreps && !disallowOffsetTransformForBreps;

         using (IFCTransaction tr = new IFCTransaction(file))
         {
            using (TransformSetter transformSetter = TransformSetter.Create())
            {
               if (useOffsetTransformForBReps)
                  bodyData.OffsetTransform = transformSetter.InitializeFromBoundingBox(exporterIFC, bbox, exportBodyParams, element.Location, instanceGeometry, out unscaledTrfOrig);

               BodyData brepBodyData = ExportBodyAsBRep(exporterIFC, geometryList, exportAsBRep, bodyItems, element, categoryId, overrideMaterialId,
                  contextOfItems, eps, options, bodyData, instanceGeometry:instanceGeometry);
               if (brepBodyData == null)
                  tr.RollBack();
               else
               {
                  XYZ lpOrig = ((bodyData != null) && (bodyData.OffsetTransform != null)) ? UnitUtil.ScaleLength(bodyData.OffsetTransform.Origin) : new XYZ();
                  transformSetter.CreateLocalPlacementFromOffset(exporterIFC, bbox, exportBodyParams, lpOrig, unscaledTrfOrig);
                  tr.Commit();
               }

               return brepBodyData;
            }
         }
      }

      /// <summary>
      /// Exports list of solids and meshes to IFC body representation.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="categoryId">The category id.</param>
      /// <param name="solids">The solids.</param>
      /// <param name="meshes">The meshes.</param>
      /// <param name="options">The settings for how to export the body.</param>
      /// <param name="exportBodyParams">The extrusion creation data.</param>
      /// <returns>The body data.</returns>
      public static BodyData ExportBody(ExporterIFC exporterIFC,
          Element element,
          ElementId categoryId,
          ElementId overrideMaterialId,
          IList<Solid> solids,
          IList<Mesh> meshes,
          BodyExporterOptions options,
          IFCExportBodyParams exportBodyParams)
      {
         IList<GeometryObject> objects = new List<GeometryObject>();
         foreach (Solid solid in solids)
            objects.Add(solid);
         foreach (Mesh mesh in meshes)
            objects.Add(mesh);

         return ExportBody(exporterIFC, element, categoryId, overrideMaterialId, objects, options, exportBodyParams);
      }

      /// <summary>
      /// Exports a geometry object to IFC body representation.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="categoryId">The category id.</param>
      /// <param name="geometryObject">The geometry object.</param>
      /// <param name="options">The settings for how to export the body.</param>
      /// <param name="exportBodyParams">The extrusion creation data.</param>
      /// <returns>The body data.</returns>
      public static BodyData ExportBody(ExporterIFC exporterIFC,
         Element element, ElementId categoryId, ElementId overrideMaterialId,
         GeometryObject geometryObject, BodyExporterOptions options,
         IFCExportBodyParams exportBodyParams)
      {
         IList<GeometryObject> geomList = new List<GeometryObject>();
         if (geometryObject is Solid)
         {
            IList<Solid> splitVolumes = GeometryUtil.SplitVolumes(geometryObject as Solid);
            foreach (Solid solid in splitVolumes)
               geomList.Add(solid);
         }
         else
            geomList.Add(geometryObject);
         return ExportBody(exporterIFC, element, categoryId, overrideMaterialId, geomList, options, exportBodyParams);
      }

      /// <summary>
      /// Exports a geometry object to IFC body representation.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="categoryId">The category id.</param>
      /// <param name="geometryElement">The geometry element.</param>
      /// <param name="options">The settings for how to export the body.</param>
      /// <param name="exportBodyParams">The extrusion creation data.</param>
      /// <param name="offsetTransform">The transform used to shift the body to the local origin.</param>
      /// <returns>The body data.</returns>
      public static BodyData ExportBody(ExporterIFC exporterIFC,
         Element element, ElementId categoryId, ElementId overrideMaterialId,
         GeometryElement geometryElement, BodyExporterOptions options,
         IFCExportBodyParams exportBodyParams)
      {
         SolidMeshGeometryInfo info = null;
         IList<GeometryObject> geomList = new List<GeometryObject>();

         if (!ExporterCacheManager.ExportOptionsCache.ExportAs2x2)
         {
            info = GeometryUtil.GetSplitSolidMeshGeometry(geometryElement);
            IList<Mesh> meshes = info.GetMeshes();
            if (meshes.Count == 0)
            {
               IList<Solid> solidList = info.GetSolids();
               geomList = FamilyExporterUtil.RemoveInvisibleSolidsAndMeshes(element.Document, exporterIFC, ref solidList, ref meshes);
            }
         }

         if (geomList.Count == 0)
            geomList.Add(geometryElement);

         return ExportBody(exporterIFC, element, categoryId, overrideMaterialId, geomList,
             options, exportBodyParams);
      }

      static BodyData SaveMaterialAndFootprintInfo(BodyData bodyData, MaterialAndProfile materialAndProfile, HashSet<FootPrintInfo> footprintInfoSet, bool collectFootprintOption)
      {
         if (materialAndProfile != null)
            bodyData.MaterialAndProfile = materialAndProfile;
         // Export of item with Footprint identifier only in IFC4
         if ((!ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4) && (footprintInfoSet.Count > 0 && collectFootprintOption))
         {
            List<CurveLoop> footprintCurveLoops = new List<CurveLoop>();
            foreach (FootPrintInfo finfo in footprintInfoSet)
            {
               footprintCurveLoops.AddRange(finfo.ExtrusionBaseLoops);
            }
            if (bodyData.FootprintInfo != null)
               footprintCurveLoops.AddRange(bodyData.FootprintInfo.ExtrusionBaseLoops);

            bodyData.FootprintInfo = new FootPrintInfo(footprintCurveLoops, bodyData.OffsetTransform);
         }

         return bodyData;
      }

      static IList<(List<List<XYZ>>, Color, double, ElementId)> GetTriangleListsFromSolid(
         ExporterIFC exporterIFC, Element element, GeometryObject geomObject, 
         BodyExporterOptions options, Transform trfToUse)
      {
         IDictionary<long, Tuple<List<List<XYZ>>, Color, double>> triangleListDict = 
            new SortedDictionary<long, Tuple<List<List<XYZ>>, Color, double>>();

         Solid geomSolid = geomObject as Solid;
         FaceArray faces = geomSolid.Faces;

         // The default tessellationLevel is -1, which is illegal for Triangulate.  Get a value in range. 
         double tessellationLevel = options.TessellationControls.LevelOfDetail;
         if (tessellationLevel < 0.0)
         {
            tessellationLevel = ((double)ExporterCacheManager.ExportOptionsCache.LevelOfDetail) / 4.0;
         }

         foreach (Face face in faces)
         {
            Mesh faceTriangulation = face.Triangulate(tessellationLevel);
            if (faceTriangulation == null)
            {
               continue;
            }

            (List<List<XYZ>> triangleList, Color color, double opacity, ElementId matId) =
               GetTriangleListFromMesh(exporterIFC, element, faceTriangulation, trfToUse, face);
            long matIdAsLong = matId.Value;
            if (!triangleListDict.TryGetValue(matIdAsLong, out Tuple<List<List<XYZ>>, Color, double> currList))
            {
               List<List<XYZ>> emptyTriangleList = new List<List<XYZ>>();
               currList = Tuple.Create(emptyTriangleList, color, opacity);
               triangleListDict[matIdAsLong] = currList;
            }
            currList.Item1.AddRange(triangleList);
         }

         IList<(List<List<XYZ>>, Color, double, ElementId)> triangleLists = new
            List<(List<List<XYZ>>, Color, double, ElementId)>();

         foreach (KeyValuePair<long, Tuple<List<List<XYZ>>, Color, double>> data in triangleListDict)
         {
            (List<List<XYZ>>, Color, double, ElementId) triangleList =
               (data.Value.Item1, data.Value.Item2, data.Value.Item3, new ElementId(data.Key));
            triangleLists.Add(triangleList);
         }

         return triangleLists;
      }

      static (List<List<XYZ>>, Color, double, ElementId) GetTriangleListFromMesh(
         ExporterIFC exporterIFC, Element element, GeometryObject geomObject, Transform trfToUse, GeometryObject parentObject)
      {
         List<List<XYZ>> triangleList = new List<List<XYZ>>();
         Mesh geomMesh = geomObject as Mesh;

         for (int ii = 0; ii < geomMesh.NumTriangles; ++ii)
         {
            List<XYZ> triangleVertices = new List<XYZ>();
            MeshTriangle triangle = geomMesh.get_Triangle(ii);
            for (int tri = 0; tri < 3; ++tri)
            {
               XYZ vert = TransformAndScalePoint(exporterIFC, triangle.get_Vertex(tri), trfToUse);
               triangleVertices.Add(vert);
            }
            triangleList.Add(triangleVertices);
         }

         (Color color, double opacity, ElementId matId) = GetBestColourAndOpacity(element, parentObject ?? geomObject);
         return (triangleList, color, opacity, matId);
      }

      static IList<double> ColorToRgb(Color color)
      {
         double blueVal = (color?.Blue ?? 127.0) / 255.0;
         double greenVal = (color?.Green ?? 127.0) / 255.0;
         double redVal = (color?.Red ?? 127.0) / 255.0;
         return new List<double>() { redVal, greenVal, blueVal };
      }

      static IFCAnyHandle ColourRgbListFromColor(IFCFile file, Color matColor)
      {
         IList<IList<double>> colourRgbList = new List<IList<double>>() { ColorToRgb(matColor) };
         return IFCInstanceExporter.CreateColourRgbList(file, colourRgbList);
      }

      static IFCAnyHandle ColourRgbListFromColors(IFCFile file, IList<Color> matColors)
      {
         IList<IList<double>> colourRgbList = new List<IList<double>>();
         foreach (Color matColor in matColors)
         {
            colourRgbList.Add(ColorToRgb(matColor));
         }
         return IFCInstanceExporter.CreateColourRgbList(file, colourRgbList);
      }

      static XYZ TransformAndScalePoint(ExporterIFC exporterIFC, XYZ vertex, Transform trf)
      {
         XYZ vertexScaled;
         if (trf == null)
            vertexScaled = ExporterIFCUtils.TransformAndScalePoint(exporterIFC, vertex);
         else
         {
            vertexScaled = trf.OfPoint(UnitUtil.ScaleLength(vertex)); // scale first since the Trf is already scaled;
         }

         return vertexScaled;
      }
   }
}