﻿//
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
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Export.Utility;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Common.Utility;
using Revit.IFC.Common.Enums;

namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// Provides methods to export curtain systems.
   /// </summary>
   class CurtainSystemExporter
   {
      /// Exports a curtain object as container.
      /// </summary>
      /// <param name="allSubElements">Collection of elements contained in the host curtain element.</param>
      /// <param name="wallElement">The curtain system element.</param>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void ExportCurtainObjectCommonAsContainer(ICollection<ElementId> allSubElements, Element wallElement,
         ExporterIFC exporterIFC, ProductWrapper origWrapper, PlacementSetter currSetter)
      {
         if (wallElement == null)
            return;

         string overrideCADLayer = RepresentationUtil.GetPresentationLayerOverride(wallElement);

         using (ExporterStateManager.CADLayerOverrideSetter layerSetter = new ExporterStateManager.CADLayerOverrideSetter(overrideCADLayer))
         {
            HashSet<ElementId> alreadyVisited = new HashSet<ElementId>();  // just in case.
            Options geomOptions = GeometryUtil.GetIFCExportGeometryOptions();
            {
               foreach (ElementId subElemId in allSubElements)
               {
                  using (ProductWrapper productWrapper = ProductWrapper.Create(origWrapper))
                  {
                     // This element has already been filtered out, don't look again.
                     if (!ExporterCacheManager.NonSpatialElements.Contains(subElemId))
                        continue;

                     Element subElem = wallElement.Document.GetElement(subElemId);
                     if (subElem == null)
                        continue;

                     if (alreadyVisited.Contains(subElemId))
                        continue;
                     alreadyVisited.Add(subElemId);

                     // Respect element visibility settings.
                     if (!ElementFilteringUtil.CanExportElement(subElem, false) || !ElementFilteringUtil.IsElementVisible(subElem))
                        continue;

                     GeometryElement geomElem = subElem.get_Geometry(geomOptions);
                     if (geomElem == null)
                        continue;

                     try
                     {
                        if (subElem is FamilyInstance)
                        {
                           IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(subElem, out _);

                           IFCAnyHandle currLocalPlacement = currSetter.LocalPlacement;
                           using (IFCExportBodyParams extraParams = new IFCExportBodyParams())
                           {
                              FamilyInstanceExporter.ExportFamilyInstanceAsMappedItem(exporterIFC,
                                 subElem as FamilyInstance, exportType, productWrapper, ElementId.InvalidElementId, null, currLocalPlacement);
                           }
                        }
                        else if (subElem is CurtainGridLine)
                        {
                           ProxyElementExporter.Export(exporterIFC, subElem, geomElem, productWrapper);
                        }
                        else if (subElem is Wall)
                        {
                           WallExporter.ExportWall(exporterIFC, null, subElem, null, ref geomElem, productWrapper);
                        }
                     }
                     catch 
                     {
                     }
                  }
               }
            }
         }
      }

      /// <summary>
      /// Exports curtain object as one Brep.
      /// </summary>
      /// <param name="allSubElements">Collection of elements contained in the host curtain element.</param>
      /// <param name="wallElement">The curtain wall element.</param>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="setter">The PlacementSetter object.</param>
      /// <param name="localPlacement">The local placement handle.</param>
      public static void ExportCurtainObjectAsOneEntity(IFCAnyHandle parentHnd,
         ICollection<ElementId> allSubElements, Element wallElement, ExporterIFC exporterIFC)
      {
         IFCAnyHandle prodDefRep = null;
         Document document = wallElement.Document;
         double eps = UnitUtil.ScaleLength(document.Application.VertexTolerance);

         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle contextOfItems = ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Body);
         
         IFCGeometryInfo info = IFCGeometryInfo.CreateFaceGeometryInfo(eps);

         ISet<IFCAnyHandle> bodyItems = new HashSet<IFCAnyHandle>();

         // Want to make sure we don't accidentally add a mullion or curtain line more than once.
         HashSet<ElementId> alreadyVisited = new HashSet<ElementId>();
         bool useFallbackBREP = true;
         Options geomOptions = GeometryUtil.GetIFCExportGeometryOptions();

         foreach (ElementId subElemId in allSubElements)
         {
            Element subElem = wallElement.Document.GetElement(subElemId);
            GeometryElement geomElem = subElem.get_Geometry(geomOptions);
            if (geomElem == null || alreadyVisited.Contains(subElemId))
            {
               continue;
            }
            alreadyVisited.Add(subElemId);


            // Export tessellated geometry when IFC4 Reference View is selected
            if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView || ExporterCacheManager.ExportOptionsCache.ExportAs4General)
            {
               BodyExporterOptions bodyExporterOptions = new BodyExporterOptions(false, ExportOptionsCache.ExportTessellationLevel.ExtraLow);
               IList<IFCAnyHandle> triFaceSet = BodyExporter.ExportBodyAsTessellatedFaceSet(exporterIFC, subElem, bodyExporterOptions, geomElem);
               if (triFaceSet != null && triFaceSet.Count > 0)
               {
                  foreach (IFCAnyHandle triFaceSetItem in triFaceSet)
                  {
                     bodyItems.Add(triFaceSetItem);
                  }
                  useFallbackBREP = false;    // no need to do Brep since it is successful
               }
            }
            // Export AdvancedFace before use fallback BREP
            else if (ExporterCacheManager.ExportOptionsCache.ExportAs4DesignTransferView)
            {
               IFCAnyHandle advancedBRep = BodyExporter.ExportBodyAsAdvancedBrep(exporterIFC, subElem, geomElem);
               if (bodyItems.AddIfNotNull(advancedBRep))
               {
                  useFallbackBREP = false;    // no need to do Brep since it is successful
               }
            }

            if (useFallbackBREP)
            {
               ExporterIFCUtils.CollectGeometryInfo(exporterIFC, info, geomElem, XYZ.Zero, false);
               HashSet<IFCAnyHandle> faces = new HashSet<IFCAnyHandle>(info.GetSurfaces());
               IFCAnyHandle outer = IFCInstanceExporter.CreateClosedShell(file, faces);

               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(outer))
               {
                  bodyItems.Add(RepresentationUtil.CreateFacetedBRep(exporterIFC, document,
                     false, outer, ElementId.InvalidElementId));
               }
            }
         }

         if (bodyItems.Count == 0)
         {
            return;
         }

         ElementId catId = CategoryUtil.GetSafeCategoryId(wallElement);
         IFCAnyHandle shapeRep;

         // Use tessellated geometry in Reference View
         if ((ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView || ExporterCacheManager.ExportOptionsCache.ExportAs4General) && !useFallbackBREP)
         {
            shapeRep = RepresentationUtil.CreateTessellatedRep(exporterIFC, wallElement, catId, contextOfItems, bodyItems, null);
         }
         else if (ExporterCacheManager.ExportOptionsCache.ExportAs4DesignTransferView && !useFallbackBREP)
         {
            shapeRep = RepresentationUtil.CreateAdvancedBRepRep(exporterIFC, wallElement, catId, contextOfItems, bodyItems, null);
         }
         else
         {
            shapeRep = RepresentationUtil.CreateBRepRep(exporterIFC, wallElement, catId, contextOfItems, bodyItems);
         }

         if (IFCAnyHandleUtil.IsNullOrHasNoValue(shapeRep))
         {
            return;
         }

         IList<IFCAnyHandle> shapeReps = new List<IFCAnyHandle>() { shapeRep };

         IFCAnyHandle boundingBoxRep = BoundingBoxExporter.ExportBoundingBox(exporterIFC, wallElement.get_Geometry(geomOptions), Transform.Identity);
         if (boundingBoxRep != null)
         {
            shapeReps.Add(boundingBoxRep);
         }

         prodDefRep = IFCInstanceExporter.CreateProductDefinitionShape(file, null, null, shapeReps);
         IFCAnyHandleUtil.SetAttribute(parentHnd, "Representation", prodDefRep);
      }

      private static readonly HashSet<IFCEntityType> AllowedContainerTypes =
         [
            IFCEntityType.IfcCurtainWall,
            IFCEntityType.IfcRamp,
            IFCEntityType.IfcRoof,
            IFCEntityType.IfcStair,
            IFCEntityType.IfcWall,
            IFCEntityType.IfcWallStandardCase
         ];

      /// <summary>
      /// Checks if the curtain element can be exported as container.
      /// </summary>
      /// <remarks>It checks if all sub elements to be exported have geometries.</remarks>
      /// <param name="allSubElements">Collection of elements contained in the host curtain element.</param>
      /// <param name="document">The Revit document.</param>
      /// <returns>True if it can be exported as container, false otherwise.</returns>
      private static bool CanExportCurtainWallAsContainer(Document document, IFCExportInfoPair exportType, 
         ICollection<ElementId> allSubElements)
      {
         if (!AllowedContainerTypes.Contains(exportType.ExportInstance))
         {
            return false;
         }

         Options geomOptions = GeometryUtil.GetIFCExportGeometryOptions();

         FilteredElementCollector collector = new FilteredElementCollector(document, allSubElements);

         List<Type> curtainWallSubElementTypes = new List<Type>()
            { typeof(FamilyInstance), typeof(CurtainGridLine), typeof(Wall) };

         ElementMulticlassFilter multiclassFilter = new ElementMulticlassFilter(curtainWallSubElementTypes, true);
         collector.WherePasses(multiclassFilter);
         ICollection<ElementId> filteredSubElemments = collector.ToElementIds();
         foreach (ElementId subElemId in filteredSubElemments)
         {
            if (document?.GetElement(subElemId)?.get_Geometry(geomOptions) == null)
               return false;
         }
         return true;
      }

      /// <summary>
      /// Export Curtain Walls and Roofs.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="exportType">The IFC entity to export to.</param>
      /// <param name="allSubElements">Collection of elements contained in the host curtain element.</param>
      /// <param name="element">The element to be exported.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      private static void ExportBase(ExporterIFC exporterIFC, IFCExportInfoPair exportType, 
         ICollection<ElementId> allSubElements, Element element, ProductWrapper wrapper)
      {
         if (exportType.IsUnKnown ||
            ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(exportType.ExportInstance))
         {
            return;
         }

         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

         PlacementSetter setter = null;

         using (ProductWrapper curtainWallSubWrapper = ProductWrapper.Create(wrapper, false))
         {
            try
            {
               Transform orientationTrf = Transform.Identity;
               IFCAnyHandle localPlacement = null;

               // Check for containment override
               setter = PlacementSetter.Create(exporterIFC, element, orientationTrf);
               localPlacement = setter.LocalPlacement;

               string objectType = NamingUtil.CreateIFCObjectName(exporterIFC, element);

               IFCAnyHandle prodRepHnd = null;
               string elemGUID = GUIDUtil.CreateGUID(element);
               IFCAnyHandle elemHnd = IFCInstanceExporter.CreateGenericIFCEntity(exportType,
                  file, element, elemGUID, ownerHistory, localPlacement, prodRepHnd);

               if (IFCAnyHandleUtil.IsNullOrHasNoValue(elemHnd))
                  return;

               wrapper.AddElement(element, elemHnd, setter, null, true, null);

               bool canExportCurtainWallAsContainer = CanExportCurtainWallAsContainer(element.Document, exportType, allSubElements); 
               if (!canExportCurtainWallAsContainer)
               {
                  ExportCurtainObjectAsOneEntity(elemHnd, allSubElements, element, exporterIFC);
               }
               else
               {
                  ExportCurtainObjectCommonAsContainer(allSubElements, element, exporterIFC, curtainWallSubWrapper, setter);
               }

               ICollection<IFCAnyHandle> relatedElementIds = curtainWallSubWrapper.GetAllObjects();
               if (relatedElementIds.Count > 0)
               {
                  string guid = GUIDUtil.CreateSubElementGUID(element, (int)IFCCurtainWallSubElements.RelAggregates);
                  HashSet<IFCAnyHandle> relatedElementIdSet = new HashSet<IFCAnyHandle>(relatedElementIds);
                  IFCInstanceExporter.CreateRelAggregates(file, guid, ownerHistory, null, null, elemHnd, relatedElementIdSet);
               }

               ExportCurtainWallType(exporterIFC, wrapper, elemHnd, element);
               SpaceBoundingElementUtil.RegisterSpaceBoundingElementHandle(exporterIFC, elemHnd, element.Id, ElementId.InvalidElementId);
            }
            finally
            {
               if (setter != null)
                  setter.Dispose();
            }
         }
      }

      /// <summary>
      /// Returns all of the active curtain panels for a CurtainGrid.
      /// </summary>
      /// <param name="curtainGrid">The CurtainGrid element.</param>
      /// <param name="document">The active document.</param>
      /// <returns>The element ids of the active curtain panels.</returns>
      /// <remarks>CurtainGrid.GetPanelIds() returns the element ids of the curtain panels that are directly contained in the CurtainGrid.
      /// Some of these panels however, are placeholders for "host" panels.  From a user point of view, the host panels are the real panels,
      /// and should replace these internal panels for export purposes.</remarks>
      public static ICollection<ElementId> GetVisiblePanelsForGrid(CurtainGrid curtainGrid,
         Document document)
      {
         ICollection<ElementId> panelIdsIn = curtainGrid.GetPanelIds();
         if (panelIdsIn == null)
            return null;

         HashSet<ElementId> visiblePanelIds = new HashSet<ElementId>();
         foreach (ElementId panelId in panelIdsIn)
         {
            Element element = document.GetElement(panelId);
            if (element == null)
               continue;

            ElementId hostPanelId = ElementId.InvalidElementId;
            if (element is Panel)
               hostPanelId = (element as Panel).FindHostPanel();

            if (hostPanelId != ElementId.InvalidElementId)
            {
               // If the host panel is itself a curtain wall, then we have to recursively collect its element ids.
               Element hostPanel = document.GetElement(hostPanelId);
               if (IsCurtainSystem(hostPanel))
               {
                  CurtainGridSet gridSet = GetCurtainGridSet(hostPanel);
                  if (gridSet == null || gridSet.Size == 0)
                  {
                     visiblePanelIds.Add(hostPanelId);
                  }
                  else
                  {
                     ICollection<ElementId> allSubElements = GetSubElements(gridSet, document);
                     visiblePanelIds.UnionWith(allSubElements);
                  }
               }
               else
                  visiblePanelIds.Add(hostPanelId);
            }
            else
               visiblePanelIds.Add(panelId);
         }

         return visiblePanelIds;
      }

      private static ICollection<ElementId> GetSubElements(CurtainGridSet gridSet,
         Document document)
      {
         HashSet<ElementId> allSubElements = new HashSet<ElementId>();
         
         foreach (CurtainGrid grid in gridSet)
         {
            allSubElements.UnionWith(GetVisiblePanelsForGrid(grid, document));
            allSubElements.UnionWith(grid.GetMullionIds());
         }

         return allSubElements;
      }

      /// <summary>
      /// Export non-legacy Curtain Walls and Roofs.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="allSubElements">Collection of elements contained in the host curtain element.</param>
      /// <param name="element">The element to be exported.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      private static void ExportBaseWithGrids(ExporterIFC exporterIFC, Element hostElement, 
         IFCExportInfoPair exportType, ProductWrapper productWrapper)
      {
         // Don't export the Curtain Wall itself, which has no useful geometry; instead export all of the GReps of the
         // mullions and panels.
         CurtainGridSet gridSet = GetCurtainGridSet(hostElement);
         if (gridSet == null)
         {
            if (hostElement is Wall)
            {
               ExportLegacyCurtainElement(exporterIFC, hostElement as Wall, productWrapper);
            }
            return;
         }

         if (gridSet.Size == 0)
         {
            return;
         }

         ICollection<ElementId> allSubElements = GetSubElements(gridSet, hostElement.Document);
         ExportBase(exporterIFC, exportType, allSubElements, hostElement, productWrapper);
      }

      /// <summary>
      /// Exports a curtain wall to IFC curtain wall.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="hostElement">The host object element to be exported.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void ExportWall(ExporterIFC exporterIFC, Wall hostElement, ProductWrapper productWrapper)
      {
         IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(hostElement, out _);
         ExportBaseWithGrids(exporterIFC, hostElement, exportType, productWrapper);
      }

      /// <summary>
      /// Exports a curtain roof to IFC curtain wall.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="hostElement">The host object element to be exported.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void ExportCurtainRoof(ExporterIFC exporterIFC, RoofBase hostElement, ProductWrapper productWrapper)
      {
         IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(hostElement, out _);
         ExportBaseWithGrids(exporterIFC, hostElement, exportType, productWrapper);
      }

      /// <summary>
      /// Exports a curtain system to IFC curtain system.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="hostElement">The curtain system element to be exported.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void ExportCurtainSystem(ExporterIFC exporterIFC, CurtainSystem curtainSystem, ProductWrapper productWrapper)
      {
         IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(curtainSystem, out _);

         // Check the intended IFC entity or type name is in the exclude list specified in the UI
         if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(exportType.ExportInstance))
            return;

         IFCFile file = exporterIFC.GetFile();
         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            ExportBaseWithGrids(exporterIFC, curtainSystem, exportType, productWrapper);
            transaction.Commit();
         }
      }

      /// <summary>
      /// Exports a legacy curtain element to IFC curtain wall.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="curtainElement">The curtain element.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void ExportLegacyCurtainElement(ExporterIFC exporterIFC, Element curtainElement, ProductWrapper productWrapper)
      {
         IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(curtainElement, out _);

         // Check the intended IFC entity or type name is in the exclude list specified in the UI
         if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(exportType.ExportInstance))
            return;

         ICollection<ElementId> allSubElements = ExporterIFCUtils.GetLegacyCurtainSubElements(curtainElement);

         IFCFile file = exporterIFC.GetFile();
         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            ExportBase(exporterIFC, exportType, allSubElements, curtainElement, productWrapper);
            transaction.Commit();
         }
      }

      /// <summary>
      /// Checks if the element is legacy curtain element.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>True if it is legacy curtain element.</returns>
      public static bool IsLegacyCurtainElement(Element element)
      {
         //for now, it is sufficient to check its category.
         return (CategoryUtil.GetSafeCategoryId(element) == new ElementId(BuiltInCategory.OST_Curtain_Systems));
      }

      /// <summary>
      /// Checks if the wall is legacy curtain wall.
      /// </summary>
      /// <param name="wall">The wall.</param>
      /// <returns>True if it is legacy curtain wall, false otherwise.</returns>
      public static bool IsLegacyCurtainWall(Wall wall)
      {
         try
         {
            CurtainGrid curtainGrid = wall.CurtainGrid;
            if (curtainGrid != null)
            {
               // The point of this code is to potentially throw an exception. If it does, we have a legacy curtain wall.
               curtainGrid.GetPanelIds();
            }
            else
               return false;
         }
         catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
         {
            if (ex.Message == "The host object is obsolete.")
               return true;
            else
               throw;
         }

         return false;
      }

      /// <summary>
      /// Returns if an element is a curtain system of any element type known to Revit API.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>True if it is a curtain system of any base element type, false otherwise.</returns>
      /// <remarks>There are some legacy types not covered here, see IsLegacyCurtainElement.</remarks>
      public static bool IsCurtainSystem(Element element)
      {
         if (element == null)
            return false;

         CurtainGridSet curtainGridSet = GetCurtainGridSet(element);
         if (curtainGridSet == null)
            return (element is Wall);
         return (curtainGridSet.Size > 0);
      }

      /// <summary>
      /// Provides a unified interface to get the curtain grids associated with an element.
      /// </summary>
      /// <param name="element">The host element.</param>
      /// <returns>A CurtainGridSet with 0 or more CurtainGrids, or null if invalid.</returns>
      public static CurtainGridSet GetCurtainGridSet(Element element)
      {
         CurtainGridSet curtainGridSet = null;
         if (element is Wall)
         {
            Wall wall = element as Wall;
            if (!IsLegacyCurtainWall(wall))
            {
               CurtainGrid curtainGrid = wall.CurtainGrid;
               curtainGridSet = new CurtainGridSet();
               if (curtainGrid != null)
                  curtainGridSet.Insert(curtainGrid);
            }
         }
         else if (element is FootPrintRoof)
         {
            FootPrintRoof footPrintRoof = element as FootPrintRoof;
            curtainGridSet = footPrintRoof.CurtainGrids;
         }
         else if (element is ExtrusionRoof)
         {
            ExtrusionRoof extrusionRoof = element as ExtrusionRoof;
            curtainGridSet = extrusionRoof.CurtainGrids;
         }
         else if (element is CurtainSystem)
         {
            CurtainSystem curtainSystem = element as CurtainSystem;
            curtainGridSet = curtainSystem.CurtainGrids;
         }

         return curtainGridSet;
      }

      /// <summary>
      /// Exports curtain wall types to IfcCurtainWallType.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="wrapper">The ProductWrapper class.</param>
      /// <param name="elementHandle">The element handle.</param>
      /// <param name="element">The element.</param>
      public static void ExportCurtainWallType(ExporterIFC exporterIFC, ProductWrapper wrapper, IFCAnyHandle elementHandle, Element element)
      {
         if (elementHandle == null || element == null)
            return;

         Document doc = element.Document;
         ElementId typeElemId = element.GetTypeId();
         ElementType elementType = doc.GetElement(typeElemId) as ElementType;
         if (elementType == null)
            return;

         IFCExportInfoPair exportType = new IFCExportInfoPair(IFCEntityType.IfcCurtainWallType);
         IFCAnyHandle wallType = ExporterCacheManager.ElementTypeToHandleCache.Find(elementType, exportType);
         if (!IFCAnyHandleUtil.IsNullOrHasNoValue(wallType))
         {
            ExporterCacheManager.TypeRelationsCache.Add(wallType, elementHandle);
            return;
         }

         string elemElementType = NamingUtil.GetElementTypeOverride(elementType, null);

         // Property sets will be set later.
         wallType = IFCInstanceExporter.CreateCurtainWallType(exporterIFC.GetFile(), elementType,
             null, null, null, elemElementType, (elemElementType != null) ? "USERDEFINED" : "NOTDEFINED");

         wrapper.RegisterHandleWithElementType(elementType, exportType, wallType, null);

         ExporterCacheManager.TypeRelationsCache.Add(wallType, elementHandle);
      }
   }
}