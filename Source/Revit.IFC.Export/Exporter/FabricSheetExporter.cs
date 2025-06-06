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
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Autodesk.Revit.DB.Structure;
using Revit.IFC.Export.Utility;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Export.Exporter.PropertySet;
using Revit.IFC.Common.Utility;
using Revit.IFC.Common.Enums;

namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// Provides methods to export a Revit element as IfcReinforcingMesh.
   /// </summary>
   public class FabricSheetExporter
   {
      /// <summary>
      /// Exports a FabricArea as an IfcGroup.  There is no geometry to export.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="element">The element.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      /// <returns>True if exported successfully, false otherwise.</returns>
      public static bool ExportFabricArea(ExporterIFC exporterIFC, Element element, ProductWrapper productWrapper)
      {
         if (element == null)
            return false;

         // Check the intended IFC entity or type name is in the exclude list specified in the UI
         Common.Enums.IFCEntityType elementClassTypeEnum = Common.Enums.IFCEntityType.IfcGroup;
         if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(elementClassTypeEnum))
            return false;

         HashSet<IFCAnyHandle> fabricSheetHandles = null;
         if (!ExporterCacheManager.FabricAreaHandleCache.TryGetValue(element.Id, out fabricSheetHandles))
            return false;

         if (fabricSheetHandles == null || fabricSheetHandles.Count == 0)
            return false;

         IFCFile file = exporterIFC.GetFile();

         using (IFCTransaction tr = new IFCTransaction(file))
         {
            string guid = GUIDUtil.CreateGUID(element);
            IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;
            string revitObjectType = NamingUtil.GetFamilyAndTypeName(element);
            string name = NamingUtil.GetNameOverride(element, revitObjectType);
            string description = NamingUtil.GetDescriptionOverride(element, null);
            string objectType = NamingUtil.GetObjectTypeOverride(element, revitObjectType);

            IFCAnyHandle fabricArea = IFCInstanceExporter.CreateGroup(file, guid,
                ownerHistory, name, description, objectType);

            IFCExportInfoPair exportInfo = new IFCExportInfoPair(IFCEntityType.IfcGroup);
            productWrapper.AddElement(element, fabricArea, exportInfo);

            string groupGuid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, fabricArea));
            IFCInstanceExporter.CreateRelAssignsToGroup(file, groupGuid, ownerHistory,
               null, null, fabricSheetHandles, null, fabricArea);

            tr.Commit();
            return true;
         }
      }

      public struct FabricSheetExportConfig
      {
         public FabricSheet Sheet { get; set; }
         public Document Doc { get; set; }
         public FabricSheetType SheetType { get; set; }
         public ISet<IFCAnyHandle> BodyItems { get; set; }
         public ElementId CategoryId { get; set; }
         public ExporterIFC ExporterIFC { get; set; }
         public IFCFile File { get; set; }
         public PlacementSetter PlacementSetter { get; set; }
         public ElementId MaterialId { get; set; }
         public ProductWrapper ProductWrapper { get; set; }
         public IFCExportBodyParams EcData { get; set; }
      }

      /// <summary>
      /// Exports an element as an IfcReinforcingMesh.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="element">The element.</param>
      /// <param name="geometryElement">The geometry element.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      /// <returns>True if exported successfully, false otherwise.</returns>
      public static bool ExportFabricSheet(ExporterIFC exporterIFC, FabricSheet sheet,
          GeometryElement geometryElement, ProductWrapper productWrapper)
      {
         if (sheet == null || geometryElement == null)
            return false;

         IFCFile file = exporterIFC.GetFile();
         using (IFCTransaction tr = new IFCTransaction(file))
         {
            if (!(sheet.Document?.GetElement(sheet.GetTypeId()) is FabricSheetType fsType))
               return false;

            using (PlacementSetter placementSetter = PlacementSetter.Create(exporterIFC, sheet, null))
            {
               using (IFCExportBodyParams ecData = new IFCExportBodyParams())
               {
                  ecData.SetLocalPlacement(placementSetter.LocalPlacement);

                  FabricSheetExportConfig config = new FabricSheetExportConfig()
                  {
                     BodyItems = new HashSet<IFCAnyHandle>(),
                     CategoryId = CategoryUtil.GetSafeCategoryId(sheet),
                     Doc = sheet.Document,
                     Sheet = sheet,
                     EcData = ecData,
                     ExporterIFC = exporterIFC,
                     File = file,
                     PlacementSetter = placementSetter,
                     ProductWrapper = productWrapper,
                     SheetType = fsType
                  };

                  ParameterUtil.GetElementIdValueFromElementOrSymbol(config.Sheet, BuiltInParameter.MATERIAL_ID_PARAM, out ElementId materialId);
                  config.MaterialId = materialId;

                  bool status = true;
                  if (config.SheetType.IsCustom())
                  {
                     if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(IFCEntityType.IfcElementAssembly))
                        return false;
                     else
                        status = ExportCustomFabricSheet(config);
                  }
                  else
                  {
                     if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(IFCEntityType.IfcReinforcingMesh))
                        return false;
                     else
                        status = ExportStandardFabricSheet(config);
                  }

                  if (!status)
                  {
                     tr.RollBack();
                     return false;
                  }
               }
            }
            tr.Commit();
            return true;
         }
      }

      private static bool ExportCustomFabricSheet(FabricSheetExportConfig cfg)
      {
         if (cfg.Equals(null) || cfg.Sheet.IsBent)
            return false;

         string guid = GUIDUtil.CreateGUID(cfg.Sheet);
         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;
         HashSet<IFCAnyHandle> rebarHandles = new HashSet<IFCAnyHandle>();
         string matName = NamingUtil.GetMaterialName(cfg.Doc, cfg.MaterialId);

         int ii = 0;
         do
         {
            WireDistributionDirection dir = (WireDistributionDirection)ii;
            IList<Curve> wireCenterlines = cfg.Sheet.GetWireCenterlines(dir);
            for (int jj = 0; jj < wireCenterlines.Count; jj++)
            {
               double wireDiam = 0.0;

               FabricWireItem wire = cfg.SheetType.GetWireItem(jj, dir);
               if (cfg.Doc.GetElement(wire.WireType) is FabricWireType wireType)
                  wireDiam = UnitUtil.ScaleLength(wireType.WireDiameter);

               IFCAnyHandle bodyItem = GeometryUtil.CreateSweptDiskSolid(cfg.ExporterIFC, cfg.File, wireCenterlines[jj], wireDiam / 2.0, null);
               RepresentationUtil.CreateStyledItemAndAssign(cfg.File, cfg.Doc, cfg.MaterialId, bodyItem);

               ISet<IFCAnyHandle> bodyItems = new HashSet<IFCAnyHandle> { bodyItem };
               IFCAnyHandle shapeRep = null;
               if (bodyItems.Count > 0)
               {
                  ElementId cfgCategoryId = CategoryUtil.GetSafeCategoryId(cfg);
                  IFCAnyHandle contextOfItems = ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Body);
                  shapeRep = RepresentationUtil.CreateAdvancedSweptSolidRep(cfg.ExporterIFC, cfg.Sheet, 
                     cfgCategoryId, contextOfItems, bodyItems, null);
               }
               IList<IFCAnyHandle> shapeReps = new List<IFCAnyHandle>();
               if (shapeRep != null)
                  shapeReps.Add(shapeRep);

               IFCAnyHandle prodRep = IFCInstanceExporter.CreateProductDefinitionShape(cfg.File, null, null, shapeReps);
               IFCAnyHandle handle = IFCInstanceExporter.CreateReinforcingBar(cfg.ExporterIFC, cfg.Sheet, guid, ExporterCacheManager.OwnerHistoryHandle,
                  cfg.PlacementSetter.LocalPlacement, prodRep, matName, wireDiam, 0, 0, IFCReinforcingBarRole.NotDefined, null);
               IFCAnyHandleUtil.SetAttribute(handle, "ObjectType", "Generic");
               CategoryUtil.CreateMaterialAssociation(cfg.ExporterIFC, handle, cfg.MaterialId);
               rebarHandles.Add(handle);
            }
            ii++;
         }
         while (ii < 2);

         IFCAnyHandle assemblyInstanceHnd = IFCInstanceExporter.CreateElementAssembly(cfg.ExporterIFC, cfg.Sheet, guid,
            ownerHistory, cfg.PlacementSetter.LocalPlacement, null, null, IFCElementAssemblyType.UserDefined);
         IFCExportInfoPair assemblyExportInfo = new IFCExportInfoPair(IFCEntityType.IfcElementAssembly);
         cfg.ProductWrapper.AddElement(cfg.Sheet, assemblyInstanceHnd, cfg.PlacementSetter.LevelInfo, null, true, assemblyExportInfo);
         ExporterCacheManager.AssemblyInstanceCache.RegisterAssemblyInstance(cfg.Sheet.Id, assemblyInstanceHnd);
         IFCInstanceExporter.CreateRelAggregates(cfg.File, guid, ownerHistory, null, null, assemblyInstanceHnd, rebarHandles);

         return true;
      }

      private static bool ExportStandardFabricSheet(FabricSheetExportConfig cfg)
      {
         if (cfg.Equals(null))
            return false;

         string guid = GUIDUtil.CreateGUID(cfg.Sheet);
         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

         int ii = 0;
         do
         {
            WireDistributionDirection dir = (WireDistributionDirection)ii;
            IList<Curve> wireCenterlines = cfg.Sheet?.GetWireCenterlines(dir);
            for (int jj = 0; jj < wireCenterlines.Count; jj++)
            {
               double wireDiam = 0.0;

               Element wireTypeElem = (dir == WireDistributionDirection.Major) ? cfg.Doc?.GetElement(cfg.SheetType?.MajorDirectionWireType) :
                  cfg.Doc?.GetElement(cfg.SheetType?.MinorDirectionWireType);

               if (wireTypeElem is FabricWireType wireType)
                  wireDiam = UnitUtil.ScaleLength(wireType.WireDiameter);
               IFCAnyHandle bodyItem = GeometryUtil.CreateSweptDiskSolid(cfg.ExporterIFC, cfg.File, wireCenterlines[jj], wireDiam / 2.0, null);

               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(bodyItem))
               {
                  cfg.BodyItems?.Add(bodyItem);
                  RepresentationUtil.CreateStyledItemAndAssign(cfg.File, cfg.Doc, cfg.MaterialId, bodyItem);
               }
            }
            ii++;
         }
         while (ii < 2);

         IFCAnyHandle shapeRep = null;
         if (cfg.BodyItems.Count > 0)
         {
            ElementId categoryId = CategoryUtil.GetSafeCategoryId(cfg);
            IFCAnyHandle contextOfItems = ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Body);
            shapeRep = RepresentationUtil.CreateAdvancedSweptSolidRep(cfg.ExporterIFC, cfg.Sheet,
               categoryId, contextOfItems, cfg.BodyItems, null);
         }

         IList<IFCAnyHandle> shapeReps = new List<IFCAnyHandle>();
         if (shapeRep != null)
            shapeReps.Add(shapeRep);

         IFCAnyHandle prodRep = (shapeReps != null) ? IFCInstanceExporter.CreateProductDefinitionShape(cfg.File, null, null, shapeReps) : null;

         FabricParams fabricParams = null;
         if (!ExporterCacheManager.FabricParamsCache.TryGetValue(cfg.Sheet.Id, out fabricParams))
         {
            fabricParams = new FabricParams(cfg.Sheet);
            ExporterCacheManager.FabricParamsCache[cfg.Sheet.Id] = fabricParams;
         }

         IFCAnyHandle handle = IFCInstanceExporter.CreateReinforcingMesh(cfg.ExporterIFC, cfg.Sheet, guid, ownerHistory, cfg.EcData.GetLocalPlacement(),
            prodRep, fabricParams.SteelGrade, fabricParams.MeshLength, fabricParams.MeshWidth,
            fabricParams.LongitudinalBarNominalDiameter, fabricParams.TransverseBarNominalDiameter,
            fabricParams.LongitudinalBarCrossSectionArea, fabricParams.TransverseBarCrossSectionArea,
            fabricParams.LongitudinalBarSpacing, fabricParams.TransverseBarSpacing);
         IFCExportInfoPair exportInfo = new IFCExportInfoPair(IFCEntityType.IfcReinforcingMesh);
         ElementId fabricAreaId = cfg.Sheet?.FabricAreaOwnerId;
         if (fabricAreaId != ElementId.InvalidElementId)
         {
            if (!ExporterCacheManager.FabricAreaHandleCache.TryGetValue(fabricAreaId, out HashSet<IFCAnyHandle> fabricSheets))
            {
               fabricSheets = new HashSet<IFCAnyHandle>();
               ExporterCacheManager.FabricAreaHandleCache[fabricAreaId] = fabricSheets;
            }
            fabricSheets.Add(handle);
         }
         cfg.ProductWrapper.AddElement(cfg.Sheet, handle, cfg.PlacementSetter?.LevelInfo, cfg.EcData, true, exportInfo);

         if (exportInfo.ExportType != Common.Enums.IFCEntityType.UnKnown)
         {
            IFCAnyHandle type = ExporterUtil.CreateGenericTypeFromElement(cfg.Sheet, exportInfo, cfg.File, cfg.ProductWrapper);
            ExporterCacheManager.TypeRelationsCache.Add(type, handle);
         }

         CategoryUtil.CreateMaterialAssociation(cfg.ExporterIFC, handle, cfg.MaterialId);

         return true;
      }
   }
}