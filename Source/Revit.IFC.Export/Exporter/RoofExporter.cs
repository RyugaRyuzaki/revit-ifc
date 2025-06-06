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
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Export.Utility;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Common.Utility;
using Revit.IFC.Common.Enums;
using System.Linq;
using Revit.IFC.Export.Exporter.PropertySet;

namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// Provides methods to export Roof elements.
   /// </summary>
   class RoofExporter
   {
      const string slabRoofPredefinedType = "ROOF";

      /// <summary>
      /// Exports a roof to IfcRoof.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="roof">The roof element.</param>
      /// <param name="geometryElement">The geometry element.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static IFCAnyHandle ExportRoof(ExporterIFC exporterIFC, Element roof, ref GeometryElement geometryElement,
          ProductWrapper productWrapper)
      {
         if (roof == null || geometryElement == null)
            return null;

         bool exportRoofAsSingleGeometry = ExporterCacheManager.ExportOptionsCache.ExportHostAsSingleEntity ||
            (productWrapper != null &&
            !ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4 &&
            IFCAnyHandleUtil.IsNullOrHasNoValue(productWrapper.GetAnElement()));

         string ifcEnumType;
         IFCExportInfoPair roofExportType = ExporterUtil.GetProductExportType(roof, out ifcEnumType);
         if (roofExportType.IsUnKnown)
         {
            roofExportType = new IFCExportInfoPair(IFCEntityType.IfcRoof, "");
         }

         MaterialLayerSetInfo layersetInfo = new MaterialLayerSetInfo(exporterIFC, roof, productWrapper);
         IFCFile file = exporterIFC.GetFile();
         Document doc = roof.Document;

         IFCAnyHandle roofHnd = null;
         using (SubTransaction tempPartTransaction = new SubTransaction(doc))
         {
            // For IFC4RV export, Element will be split into its parts(temporarily) in order to export the wall by its parts
            // If Parts are created by code and not by user then their name should be equal to Material name.
            ExporterUtil.ExportPartAs exportPartAs = ExporterUtil.CanExportParts(roof);
            bool exportParts = exportPartAs == ExporterUtil.ExportPartAs.Part && !exportRoofAsSingleGeometry;
            bool exportByComponents = exportPartAs == ExporterUtil.ExportPartAs.ShapeAspect;
            ExporterCacheManager.TemporaryPartsCache.SetPartExportType(roof.Id, exportPartAs);

            using (IFCTransaction tr = new IFCTransaction(file))
            {
               using (PlacementSetter placementSetter = PlacementSetter.Create(exporterIFC, roof, null))
               {
                  using (IFCExportBodyParams ecData = new IFCExportBodyParams())
                  {
                     // If the roof is an in-place family, we will allow any arbitrary orientation.  While this may result in some
                     // in-place "cubes" exporting with the wrong direction, it is unlikely that an in-place family would be
                     // used for this reason in the first place.
                     ecData.PossibleExtrusionAxes = (roof is FamilyInstance) ? IFCExtrusionAxes.TryXYZ : IFCExtrusionAxes.TryZ;
                     ecData.AreInnerRegionsOpenings = true;
                     ecData.SetLocalPlacement(placementSetter.LocalPlacement);

                     ElementId categoryId = CategoryUtil.GetSafeCategoryId(roof);

                     BodyExporterOptions bodyExporterOptions = new BodyExporterOptions(true, ExportOptionsCache.ExportTessellationLevel.ExtraLow);
                     BodyData bodyData = null;
                     IFCAnyHandle prodRep = null;
                     IList<IFCAnyHandle> representations = new List<IFCAnyHandle>();
                     IList<ElementId> materialIds = new List<ElementId>();

                     if (!exportByComponents)
                     {
                        prodRep = RepresentationUtil.CreateAppropriateProductDefinitionShape(exporterIFC, roof,
                            categoryId, geometryElement, bodyExporterOptions, null, ecData, out bodyData, instanceGeometry: true);
                        if (bodyData != null && bodyData.MaterialIds != null)
                           materialIds = bodyData.MaterialIds;
                        if (IFCAnyHandleUtil.IsNullOrHasNoValue(prodRep))
                        {
                           ecData.ClearOpenings();
                           return null;
                        }
                     }

                     bool exportSlab = ((ecData.ScaledLength > MathUtil.Eps() || exportByComponents) &&
                        roofExportType.ExportInstance == IFCEntityType.IfcRoof && !exportRoofAsSingleGeometry);

                     string guid = GUIDUtil.CreateGUID(roof);
                     IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;
                     IFCAnyHandle localPlacement = ecData.GetLocalPlacement();

                     if (exportByComponents)
                     {
                        if (exportSlab || exportRoofAsSingleGeometry)
                        {
                           prodRep = RepresentationUtil.CreateProductDefinitionShapeWithoutBodyRep(exporterIFC, roof, categoryId, geometryElement, representations);
                           IFCAnyHandle hostShapeRepFromParts = PartExporter.ExportHostPartAsShapeAspects(exporterIFC, 
                              roof, prodRep, ElementId.InvalidElementId, layersetInfo, ecData);
                        }
                        else
                        {
                           ecData.ClearOpenings();
                           return null;
                        }
                     }

                     roofHnd = IFCInstanceExporter.CreateGenericIFCEntity(roofExportType, file, roof, guid, 
                        ownerHistory, localPlacement, exportSlab ? null : prodRep);

                     IFCAnyHandle typeHnd = ExporterUtil.CreateGenericTypeFromElement(roof,
                        roofExportType, file, productWrapper);
                     ExporterCacheManager.TypeRelationsCache.Add(typeHnd, roofHnd);

                     productWrapper.AddElement(roof, roofHnd, placementSetter.LevelInfo, ecData, true, roofExportType);

                     if (!(roof is RoofBase))
                        CategoryUtil.CreateMaterialAssociation(exporterIFC, roof, roofHnd, materialIds);

                     Transform offsetTransform = (bodyData != null) ? bodyData.OffsetTransform : Transform.Identity;

                     if (exportSlab)
                     {
                        string slabGUID = GUIDUtil.CreateSubElementGUID(roof, (int)IFCRoofSubElements.RoofSlabStart);
                        IFCAnyHandle slabLocalPlacementHnd = ExporterUtil.CopyLocalPlacement(file, localPlacement);
                        string slabName = IFCAnyHandleUtil.GetStringAttribute(roofHnd, "Name") + ":1";

                        IFCAnyHandle slabHnd = IFCInstanceExporter.CreateSlab(exporterIFC, roof, slabGUID, ownerHistory,
                           slabLocalPlacementHnd, prodRep, slabRoofPredefinedType);
                        IFCAnyHandleUtil.OverrideNameAttribute(slabHnd, slabName);

                        OpeningUtil.CreateOpeningsIfNecessary(slabHnd, roof, ecData, offsetTransform,
                            exporterIFC, slabLocalPlacementHnd, placementSetter, productWrapper);

                        ExporterUtil.RelateObject(exporterIFC, roofHnd, slabHnd);
                        IFCExportInfoPair slabRoofExportType = new IFCExportInfoPair(IFCEntityType.IfcSlab, slabRoofPredefinedType);

                        productWrapper.AddElement(null, slabHnd, placementSetter.LevelInfo, ecData, false, slabRoofExportType);

                        // Create type
                        IFCAnyHandle slabRoofTypeHnd = ExporterUtil.CreateGenericTypeFromElement(roof, slabRoofExportType, exporterIFC.GetFile(), productWrapper);
                        ExporterCacheManager.TypeRelationsCache.Add(slabRoofTypeHnd, slabHnd);

                        ExporterUtil.AddIntoComplexPropertyCache(slabHnd, layersetInfo);
                        // For earlier than IFC4 version of IFC export, the material association will be done at the Roof host level with MaterialSetUsage
                        // This one is only for IFC4 and above
                        if ((roof is RoofBase) && !ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
                        {
                           if (layersetInfo != null && !IFCAnyHandleUtil.IsNullOrHasNoValue(layersetInfo.MaterialLayerSetHandle))
                           {
                              CategoryUtil.CreateMaterialAssociation(slabHnd, layersetInfo.MaterialLayerSetHandle);
                           }
                           else if (bodyData != null)
                           {
                              CategoryUtil.CreateMaterialAssociation(exporterIFC, roof, slabHnd, bodyData.MaterialIds);
                           }
                        }
                     }
                     else
                     {
                        OpeningUtil.CreateOpeningsIfNecessary(roofHnd, roof, ecData, offsetTransform,
                           exporterIFC, localPlacement, placementSetter, productWrapper);

                        // For earlier than IFC4 version of IFC export, the material association will be done at the Roof host level with MaterialSetUsage
                        // This one is only for IFC4 and above
                        if ((roof is RoofBase) && !ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
                        {
                           if (layersetInfo != null && !IFCAnyHandleUtil.IsNullOrHasNoValue(layersetInfo.MaterialLayerSetHandle))
                           {
                              CategoryUtil.CreateMaterialAssociation(roofHnd, layersetInfo.MaterialLayerSetHandle);
                           }
                           else if (layersetInfo != null && layersetInfo.MaterialIds != null)
                           {
                              materialIds = layersetInfo.MaterialIds.Select(x => x.BaseMatId).ToList();
                              CategoryUtil.CreateMaterialAssociation(exporterIFC, roof, roofHnd, materialIds);
                           }
                        }
                     }
                  }
                  tr.Commit();
               }
            }
         }

         return roofHnd;
      }

      /// <summary>
      /// Exports a roof element to the appropriate IFC entity.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="roof">The roof element.</param>
      /// <param name="geometryElement">The geometry element.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void Export(ExporterIFC exporterIFC, RoofBase roof, ref GeometryElement geometryElement, 
         ProductWrapper productWrapper)
      {
         IFCFile file = exporterIFC.GetFile();
         using (IFCTransaction tr = new IFCTransaction(file))
         {
            // export parts or not
            bool exportParts = PartExporter.CanExportParts(roof);
            bool isCurtainRoof = CurtainSystemExporter.IsCurtainSystem(roof);

            if (exportParts)
            {
               if (!PartExporter.CanExportElementInPartExport(roof, roof.LevelId, false))
                  return;
               ExportRoofAsParts(exporterIFC, roof, geometryElement, productWrapper); // Right now, only flat roof could have parts.
            }
            else if (isCurtainRoof)
            {
               CurtainSystemExporter.ExportCurtainRoof(exporterIFC, roof, productWrapper);
            }
            else
            {
               IFCExportInfoPair roofExportType = ExporterUtil.GetProductExportType(roof, out _);

               IFCAnyHandle roofHnd = null;
               if (roofExportType.ExportInstance != IFCEntityType.IfcRoof)
               {
                  roofHnd = ExportRoof(exporterIFC, roof, ref geometryElement, productWrapper);
               }
               else
               {
                  roofHnd = ExportRoofOrFloorAsContainer(exporterIFC, roof,
                      geometryElement, productWrapper);
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(roofHnd))
                  {
                     roofHnd = ExportRoof(exporterIFC, roof, ref geometryElement, productWrapper);
                  }
               }

               // call for host objects; curtain roofs excused from call (no material information)
               if (!ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
                  HostObjectExporter.ExportHostObjectMaterials(exporterIFC, roof, roofHnd,
                      geometryElement, productWrapper, ElementId.InvalidElementId, IFCLayerSetDirection.Axis3, null, null);
            }
            tr.Commit();
         }
      }

      /// <summary>
      /// Exports a roof or floor as a container of multiple roof slabs.  Returns the handle, 
      /// if successful.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="element">The roof or floor element.</param>
      /// <param name="geometry">The geometry of the element.</param>
      /// <param name="productWrapper">The product wrapper.</param>
      /// <returns>The roof handle.</returns>
      /// <remarks>For floors, if there is only one component, return null, as we do not want to 
      /// create a container.</remarks>
      public static IFCAnyHandle ExportRoofOrFloorAsContainer(ExporterIFC exporterIFC, 
         Element element, GeometryElement geometry, ProductWrapper productWrapper)
      {
         IFCFile file = exporterIFC.GetFile();

         // We support ExtrusionRoofs, FootPrintRoofs, and Floors only.
         bool elementIsRoof = (element is ExtrusionRoof) || (element is FootPrintRoof);
         bool elementIsFloor = (element is Floor);
         if (!elementIsRoof && !elementIsFloor)
            return null;

         IFCExportInfoPair roofExportType = ExporterUtil.GetProductExportType(element, out _);
         if (roofExportType.IsUnKnown)
         {
            IFCEntityType elementClassTypeEnum = 
               elementIsFloor ? IFCEntityType.IfcSlab: IFCEntityType.IfcRoof;
            roofExportType = new IFCExportInfoPair(elementClassTypeEnum, "");
         }

         // Check the intended IFC entity or type name is in the exclude list specified in the UI
         if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(roofExportType.ExportType))
            return null;

         Document doc = element.Document;
         using (SubTransaction tempPartTransaction = new SubTransaction(doc))
         {
            using (IFCTransaction transaction = new IFCTransaction(file))
            {
               MaterialLayerSetInfo layersetInfo = new MaterialLayerSetInfo(exporterIFC, element, productWrapper);
               bool hasLayers = (layersetInfo.MaterialIds.Count > 1);
               bool exportByComponents = ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView && hasLayers;

               // We want to delay creating entity handles until as late as possible, so that if we
               // abort the IFC transaction, we don't have to delete elements.  This is both for
               // performance reasons and to potentially extend the number of projects that can be
               // exported by reducing (a small amount) of waste.
               IList<HostObjectSubcomponentInfo> hostObjectSubcomponents = null;
               try
               {
                  hostObjectSubcomponents = ExporterIFCUtils.ComputeSubcomponents(element as HostObject);
                  if (hostObjectSubcomponents == null)
                     return null;
               }
               catch
               {
                  return null;
               }

               bool createSubcomponents = !ExporterCacheManager.ExportOptionsCache.ExportHostAsSingleEntity;

               int numSubcomponents = hostObjectSubcomponents.Count;
               if (numSubcomponents == 0 || (elementIsFloor && numSubcomponents == 1))
               {
                  return null;
               }

               // TODO: If we find exactly 1 sub-component and we are exporting a floor, then we will
               // continue, but with createSubcomponents set to false.
               // if (createSubcomponents && elementIsFloor && numSubcomponents == 1) createSubcomponents = false;

               using (PlacementSetter setter = PlacementSetter.Create(exporterIFC, element, null))
               {
                  IFCAnyHandle localPlacement = setter.LocalPlacement;

                  IFCAnyHandle hostObjectHandle = null;
                  try
                  {
                     using (IFCExportBodyParams extrusionCreationData = new IFCExportBodyParams())
                     {
                        IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;
                        extrusionCreationData.SetLocalPlacement(localPlacement);
                        extrusionCreationData.ReuseLocalPlacement = true;

                        using (TransformSetter trfSetter = TransformSetter.Create())
                        {
                           IList<GeometryObject> geometryList = new List<GeometryObject>() { geometry };
                           trfSetter.InitializeFromBoundingBox(exporterIFC, geometryList, extrusionCreationData);

                           // If element is floor, then the profile curve loop of hostObjectSubComponent is computed from the top face of the floor
                           // else if element is roof, then the profile curve loop is taken from the bottom face of the roof instead 
                           XYZ extrusionDir = elementIsFloor ? -XYZ.BasisZ : XYZ.BasisZ;
                           bool reverseMaterialList = !elementIsFloor;

                           ElementId catId = CategoryUtil.GetSafeCategoryId(element);

                           IList<IFCAnyHandle> slabHandles = new List<IFCAnyHandle>();

                           IList<CurveLoop> hostObjectOpeningLoops = new List<CurveLoop>();
                           double maximumScaledDepth = 0.0;

                           int loopNum = 0;
                           int subElementStart = elementIsRoof ? (int)IFCRoofSubElements.RoofSlabStart : (int)IFCSlabSubElements.SubSlabStart;

                           // Figure out the appropriate slabExportType from the main handle.
                           IFCExportInfoPair subInfoPair;
                           switch (roofExportType.ExportInstance)
                           {
                              case IFCEntityType.IfcRoof:
                                 subInfoPair = new IFCExportInfoPair(IFCEntityType.IfcSlab, "Roof");
                                 break;
                              case IFCEntityType.IfcSlab:
                                 subInfoPair = roofExportType;
                                 break;
                              default:
                                 subInfoPair = new IFCExportInfoPair(IFCEntityType.IfcBuildingElementPart);
                                 break;
                           }

                           List<IFCAnyHandle> hostBodyItems = new List<IFCAnyHandle>();
                           IFCAnyHandle contextOfItems = ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Body);
                           IList<IFCAnyHandle> elementHandles = new List<IFCAnyHandle>();

                           foreach (HostObjectSubcomponentInfo hostObjectSubcomponent in hostObjectSubcomponents)
                           {
                              IFCExportBodyParams slabExtrusionCreationData = null;

                              if (createSubcomponents)
                              {
                                 slabExtrusionCreationData = new IFCExportBodyParams();

                                 slabExtrusionCreationData.SetLocalPlacement(extrusionCreationData.GetLocalPlacement());
                                 slabExtrusionCreationData.ReuseLocalPlacement = false;
                                 slabExtrusionCreationData.ForceOffset = true;

                                 trfSetter.InitializeFromBoundingBox(exporterIFC, geometryList, slabExtrusionCreationData);
                              }

                              Plane plane = hostObjectSubcomponent.GetPlane();
                              Transform lcs = GeometryUtil.CreateTransformFromPlane(plane);

                              IList<CurveLoop> curveLoops = new List<CurveLoop>();
                              CurveLoop slabCurveLoop = hostObjectSubcomponent.GetCurveLoop();
                              curveLoops.Add(slabCurveLoop);

                              double slope = Math.Abs(plane.Normal.Z);
                              double scaledDepth = UnitUtil.ScaleLength(hostObjectSubcomponent.Depth);
                              double scaledExtrusionDepth = scaledDepth * slope;
                              IList<string> matLayerNames = new List<string>();

                              // Create representation items based on the layers
                              // Because in this case, the Roof components are not derived from Parts, but by "splitting" geometry part that can be extruded,
                              //    the creation of the Items for IFC4RV will be different by using "manual" split based on the layer thickness
                              IList<IFCAnyHandle> bodyItems = new List<IFCAnyHandle>();
                              if (!exportByComponents)
                              {
                                 IFCAnyHandle itemShapeRep =
                                    ExtrusionExporter.CreateExtrudedSolidFromCurveLoop(exporterIFC,
                                    null, curveLoops, lcs, extrusionDir, scaledExtrusionDepth, false,
                                    out IList<CurveLoop> validatedCurveLoops);
                                 if (IFCAnyHandleUtil.IsNullOrHasNoValue(itemShapeRep))
                                 {
                                    productWrapper.ClearInternalHandleWrapperData(element);
                                    if ((validatedCurveLoops?.Count ?? 0) == 0) continue;

                                    return null;
                                 }
                                 ElementId matId = HostObjectExporter.GetFirstLayerMaterialId(element as HostObject);
                                 BodyExporter.CreateSurfaceStyleForRepItem(exporterIFC,
                                    element.Document, false, itemShapeRep, matId);
                                 bodyItems.Add(itemShapeRep);
                              }
                              else
                              {
                                 IList<MaterialLayerSetInfo.MaterialInfo> materialIds = reverseMaterialList ?
                                    layersetInfo.MaterialIds.Reverse<MaterialLayerSetInfo.MaterialInfo>().ToList() : layersetInfo.MaterialIds;

                                 double offsetDirection = extrusionDir.DotProduct(plane.Normal) > MathUtil.Eps() ? 1.0 : -1.0;
                                 foreach (MaterialLayerSetInfo.MaterialInfo matLayerInfo in materialIds)
                                 {
                                    double itemExtrDepth = matLayerInfo.Width;
                                    double scaledItemExtrDepth = UnitUtil.ScaleLength(itemExtrDepth) * slope;
                                    IFCAnyHandle itemShapeRep = ExtrusionExporter.CreateExtrudedSolidFromCurveLoop(exporterIFC, null, curveLoops, lcs, extrusionDir, scaledItemExtrDepth, false, out _);
                                    if (IFCAnyHandleUtil.IsNullOrHasNoValue(itemShapeRep))
                                    {
                                       productWrapper.ClearInternalHandleWrapperData(element);
                                       return null;
                                    }

                                    BodyExporter.CreateSurfaceStyleForRepItem(exporterIFC, element.Document, false, itemShapeRep, matLayerInfo.BaseMatId);

                                    bodyItems.Add(itemShapeRep);
                                    matLayerNames.Add(matLayerInfo.LayerName);

                                    XYZ offset = new XYZ(0, 0, itemExtrDepth * offsetDirection);   // offset is calculated as extent in the direction of extrusion
                                    lcs.Origin += offset;
                                 }
                              }

                              if (createSubcomponents)
                              {
                                 IList<IFCAnyHandle> shapeReps = new List<IFCAnyHandle>
                                 {
                                    RepresentationUtil.CreateSweptSolidRep(exporterIFC, element,
                                    catId, contextOfItems, bodyItems.ToHashSet(), null, null)
                                 };

                                 IFCAnyHandle prodDefShape =
                                    IFCInstanceExporter.CreateProductDefinitionShape(file, null, null,
                                    shapeReps);

                                 if (exportByComponents)
                                 {
                                    string representationType = ShapeRepresentationType.SweptSolid.ToString();
                                    int count = bodyItems.Count();
                                    for (int ii = 0; ii < count; ii++)
                                    {
                                       RepresentationUtil.CreateRepForShapeAspect(exporterIFC, element,
                                          prodDefShape, representationType, matLayerNames[ii],
                                          bodyItems[ii]);
                                    }
                                 }

                                 // We could replace the code below to just use the newer, and better, 
                                 // GenerateIFCGuidFrom.  The code below maintains compatibility with older
                                 // versions while generating a stable GUID for all slabs (in the unlikely
                                 // case that we have more than 255 of them).
                                 string slabGUID = (loopNum < 256) ?
                                    GUIDUtil.CreateSubElementGUID(element, subElementStart + loopNum) :
                                    GUIDUtil.GenerateIFCGuidFrom(
                                       GUIDUtil.CreateGUIDString(element, "Slab: " + loopNum.ToString()));

                                 IFCAnyHandle slabPlacement = ExporterUtil.CreateLocalPlacement(file, slabExtrusionCreationData.GetLocalPlacement(), null);
                                 IFCAnyHandle slabHnd = IFCInstanceExporter.CreateGenericIFCEntity(
                                    subInfoPair, file, element, slabGUID, ownerHistory,
                                    slabPlacement, prodDefShape);

                                 //slab quantities
                                 slabExtrusionCreationData.ScaledLength = scaledExtrusionDepth;
                                 slabExtrusionCreationData.ScaledArea = UnitUtil.ScaleArea(UnitUtil.ScaleArea(hostObjectSubcomponent.AreaOfCurveLoop));
                                 slabExtrusionCreationData.ScaledOuterPerimeter = UnitUtil.ScaleLength(curveLoops[0].GetExactLength());
                                 slabExtrusionCreationData.Slope = UnitUtil.ScaleAngle(MathUtil.SafeAcos(Math.Abs(slope)));

                                 if (ExporterCacheManager.ExportIFCBaseQuantities())
                                    PropertyUtil.CreateSlabBaseQuantities(exporterIFC, slabHnd, slabExtrusionCreationData, curveLoops[0]);

                                 productWrapper.AddElement(null, slabHnd, setter, slabExtrusionCreationData, false, roofExportType);

                                 // Create type
                                 IFCAnyHandle slabRoofTypeHnd = ExporterUtil.CreateGenericTypeFromElement(element,
                                    subInfoPair, exporterIFC.GetFile(), productWrapper);
                                 ExporterCacheManager.TypeRelationsCache.Add(slabRoofTypeHnd, slabHnd);

                                 elementHandles.Add(slabHnd);
                                 slabHandles.Add(slabHnd);

                                 hostObjectOpeningLoops.Add(slabCurveLoop);
                                 maximumScaledDepth = Math.Max(maximumScaledDepth, scaledDepth);
                                 loopNum++;

                                 ExporterUtil.AddIntoComplexPropertyCache(slabHnd, layersetInfo);

                                 // Create material association here
                                 if (layersetInfo != null && !IFCAnyHandleUtil.IsNullOrHasNoValue(layersetInfo.MaterialLayerSetHandle))
                                 {
                                    CategoryUtil.CreateMaterialAssociation(slabHnd, layersetInfo.MaterialLayerSetHandle);
                                 }
                              }
                              else
                              {
                                 hostBodyItems.AddRange(bodyItems);
                              }
                           }

                           IFCAnyHandle hostProdDefShape = null;
                           if (hostBodyItems.Count() > 0)
                           {
                              IList<IFCAnyHandle> shapeReps = new List<IFCAnyHandle>
                              {
                                 RepresentationUtil.CreateSweptSolidRep(exporterIFC, element,
                                    catId, contextOfItems, hostBodyItems.ToHashSet(), null, null)
                              };

                              hostProdDefShape = IFCInstanceExporter.CreateProductDefinitionShape(
                                 file, null, null, shapeReps);
                           }

                           string elementGUID = GUIDUtil.CreateGUID(element);

                           hostObjectHandle = IFCInstanceExporter.CreateGenericIFCEntity(
                              roofExportType, file, element, elementGUID, ownerHistory,
                              localPlacement, hostProdDefShape);

                           elementHandles.Add(hostObjectHandle);

                           productWrapper.AddElement(element, hostObjectHandle, setter, extrusionCreationData, true, roofExportType);

                           if ((slabHandles?.Count ?? 0) > 0)
                           {
                              ExporterUtil.RelateObjects(exporterIFC, null, hostObjectHandle, slabHandles);
                           }

                           int noOpening = OpeningUtil.AddOpeningsToElement(exporterIFC, elementHandles, hostObjectOpeningLoops, element, null, maximumScaledDepth,
                               null, setter, localPlacement, productWrapper);

                           transaction.Commit();
                           return hostObjectHandle;
                        }
                     }
                  }
                  catch
                  {
                     // Something wrong with the above process, unable to create the
                     // extrusion data. Reset any internal handles that may have been
                     // partially created since they are not committed.
                     // TODO: Clear out any created GUIDs, since doing an alternate approach
                     // will result in incorrect "reuse" of GUIDs.
                     productWrapper.ClearInternalHandleWrapperData(element);
                     return null;
                  }
                  finally
                  {
                     exporterIFC.ClearFaceWithElementHandleMap();
                  }
               }
            }
         }
      }

      /// <summary>
      /// Export the roof to IfcRoof containing its parts.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="element">The roof element.</param>
      /// <param name="geometryElement">The geometry element.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void ExportRoofAsParts(ExporterIFC exporterIFC, Element element, GeometryElement geometryElement, ProductWrapper productWrapper)
      {
         string ifcEnumType;
         IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(element, out ifcEnumType);
         if (exportType.IsUnKnown)
            exportType = new IFCExportInfoPair(IFCEntityType.IfcRoof);

         // Check the intended IFC entity or type name is in the exclude list specified in the UI
         if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(exportType.ExportType))
            return;

         IFCFile file = exporterIFC.GetFile();

         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            using (PlacementSetter setter = PlacementSetter.Create(exporterIFC, element, null))
            {
               IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;
               IFCAnyHandle localPlacement = setter.LocalPlacement;

               IFCAnyHandle prodRepHnd = null;

               string elementGUID = GUIDUtil.CreateGUID(element);

               IFCAnyHandle roofHandle = IFCInstanceExporter.CreateGenericIFCEntity(
                  exportType, file, 
                  element, elementGUID, ownerHistory,
                  localPlacement, prodRepHnd);
               
               // Export the parts
               PartExporter.ExportHostPart(exporterIFC, element, roofHandle, setter, localPlacement, null);

               productWrapper.AddElement(element, roofHandle, setter, null, true, exportType);

               transaction.Commit();
            }
         }
      }

      /// <summary>
      /// Gets IFCRoofType from roof type name.
      /// </summary>
      /// <param name="roofTypeName">The roof type name.</param>
      /// <returns>The IFCRoofType.</returns>
      public static string GetIFCRoofType(string roofTypeName)
      {
         string typeName = NamingUtil.RemoveSpacesAndUnderscores(roofTypeName);

         if (string.Compare(typeName, "ROOFTYPEENUM", true) == 0 ||
             string.Compare(typeName, "ROOFTYPEENUMFREEFORM", true) == 0)
            return "FREEFORM";
         if (string.Compare(typeName, "FLAT", true) == 0 ||
             string.Compare(typeName, "FLATROOF", true) == 0)
            return "FLAT_ROOF";
         if (string.Compare(typeName, "SHED", true) == 0 ||
             string.Compare(typeName, "SHEDROOF", true) == 0)
            return "SHED_ROOF";
         if (string.Compare(typeName, "GABLE", true) == 0 ||
             string.Compare(typeName, "GABLEROOF", true) == 0)
            return "GABLE_ROOF";
         if (string.Compare(typeName, "HIP", true) == 0 ||
             string.Compare(typeName, "HIPROOF", true) == 0)
            return "HIP_ROOF";
         if (string.Compare(typeName, "HIPPED_GABLE", true) == 0 ||
             string.Compare(typeName, "HIPPED_GABLEROOF", true) == 0)
            return "HIPPED_GABLE_ROOF";
         if (string.Compare(typeName, "MANSARD", true) == 0 ||
             string.Compare(typeName, "MANSARDROOF", true) == 0)
            return "MANSARD_ROOF";
         if (string.Compare(typeName, "BARREL", true) == 0 ||
             string.Compare(typeName, "BARRELROOF", true) == 0)
            return "BARREL_ROOF";
         if (string.Compare(typeName, "BUTTERFLY", true) == 0 ||
             string.Compare(typeName, "BUTTERFLYROOF", true) == 0)
            return "BUTTERFLY_ROOF";
         if (string.Compare(typeName, "PAVILION", true) == 0 ||
             string.Compare(typeName, "PAVILIONROOF", true) == 0)
            return "PAVILION_ROOF";
         if (string.Compare(typeName, "DOME", true) == 0 ||
             string.Compare(typeName, "DOMEROOF", true) == 0)
            return "DOME_ROOF";

         return typeName;        //return unchanged. Validation for ENUM will be done later specific to schema version
      }
   }
}