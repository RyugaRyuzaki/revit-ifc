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
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;
using Revit.IFC.Export.Utility;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Export.Exporter.PropertySet;
using static Revit.IFC.Export.Utility.ParameterUtil;

namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// Provides methods to export ramps
   /// </summary>
   class RampExporter
   {
      /// <summary>
      /// Checks if exporting an element of Ramp category.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>True if element is of category OST_Ramps.</returns>
      static public bool IsRamp(Element element)
      {
         // FaceWall should be exported as IfcWall.
         return (CategoryUtil.GetSafeCategoryId(element) == new ElementId(BuiltInCategory.OST_Ramps));
      }

      static private double GetDefaultHeightForRamp()
      {
         // The default height for ramps is 3'.
         return UnitUtil.ScaleLength(3.0);
      }

      /// <summary>
      /// Gets the ramp height for a ramp.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>The unscaled height.</returns>
      static public double GetRampHeight(Element element)
      {
         // Re-use the code for stairs height for legacy stairs.
         return StairsExporter.GetStairsHeightForLegacyStair(element, GetDefaultHeightForRamp());
      }

      /// <summary>
      /// Gets the number of flights of a multi-story ramp.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="element">The element.</param>
      /// <returns>The number of flights (at least 1.)</returns>
      static public int GetNumFlightsForRamp(Element element)
      {
         return StairsExporter.GetNumFlightsForLegacyStair(element, GetDefaultHeightForRamp());
      }

      /// <summary>
      /// Gets IFCRampType from ramp type name.
      /// </summary>
      /// <param name="rampTypeName">The ramp type name.</param>
      /// <returns>The IFCRampType.</returns>
      public static string GetIFCRampType(string rampTypeName)
      {
         string typeName = NamingUtil.RemoveSpacesAndUnderscores(rampTypeName);

         if (string.Compare(typeName, "StraightRun", true) == 0 ||
             string.Compare(typeName, "StraightRunRamp", true) == 0)
            return "Straight_Run_Ramp";
         if (string.Compare(typeName, "TwoStraightRun", true) == 0 ||
             string.Compare(typeName, "TwoStraightRunRamp", true) == 0)
            return "Two_Straight_Run_Ramp";
         if (string.Compare(typeName, "QuarterTurn", true) == 0 ||
             string.Compare(typeName, "QuarterTurnRamp", true) == 0)
            return "Quarter_Turn_Ramp";
         if (string.Compare(typeName, "TwoQuarterTurn", true) == 0 ||
             string.Compare(typeName, "TwoQuarterTurnRamp", true) == 0)
            return "Two_Quarter_Turn_Ramp";
         if (string.Compare(typeName, "HalfTurn", true) == 0 ||
             string.Compare(typeName, "HalfTurnRamp", true) == 0)
            return "Half_Turn_Ramp";
         if (string.Compare(typeName, "Spiral", true) == 0 ||
             string.Compare(typeName, "SpiralRamp", true) == 0)
            return "Spiral_Ramp";
         if (string.Compare(typeName, "UserDefined", true) == 0)
            return "UserDefined";

         return "NotDefined";
      }

      /// <summary>
      /// Exports the top stories of a multistory ramp.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="ramp">The ramp element.</param>
      /// <param name="numFlights">The number of flights for a multistory ramp.</param>
      /// <param name="rampHnd">The stairs container handle.</param>
      /// <param name="components">The components handles.</param>
      /// <param name="ecData">The extrusion creation data.</param>
      /// <param name="componentECData">The extrusion creation data for the components.</param>
      /// <param name="placementSetter">The placement setter.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void ExportMultistoryRamp(ExporterIFC exporterIFC, Element ramp, int numFlights,
          IFCAnyHandle rampHnd, IList<IFCAnyHandle> components, IList<IFCExportBodyParams> componentECData,
          PlacementSetter placementSetter, ProductWrapper productWrapper)
      {
         if (numFlights < 2)
            return;

         double heightNonScaled = GetRampHeight(ramp);
         if (heightNonScaled < MathUtil.Eps())
            return;

         if (IFCAnyHandleUtil.IsNullOrHasNoValue(rampHnd))
            return;

         IFCAnyHandle localPlacement = IFCAnyHandleUtil.GetObjectPlacement(rampHnd);
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(localPlacement))
            return;

         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

         IFCFile file = exporterIFC.GetFile();
         Document doc = ramp.Document;

         IFCAnyHandle relPlacement = GeometryUtil.GetRelativePlacementFromLocalPlacement(localPlacement);
         IFCAnyHandle ptHnd = IFCAnyHandleUtil.GetLocation(relPlacement);
         IList<double> origCoords = IFCAnyHandleUtil.GetCoordinates(ptHnd);

         IList<IFCAnyHandle> rampLocalPlacementHnds = new List<IFCAnyHandle>();
         IList<IFCLevelInfo> levelInfos = new List<IFCLevelInfo>();
         for (int ii = 0; ii < numFlights - 1; ii++)
         {
            IFCAnyHandle newLevelHnd = null;

            // We are going to avoid internal scaling routines, and instead scale in .NET.
            double newOffsetUnscaled = 0.0;
            IFCLevelInfo currLevelInfo =
                placementSetter.GetOffsetLevelInfoAndHandle(heightNonScaled * (ii + 1), 1.0, doc, out newLevelHnd, out newOffsetUnscaled);
            double newOffsetScaled = UnitUtil.ScaleLength(newOffsetUnscaled);

            levelInfos.Add(currLevelInfo ?? placementSetter.LevelInfo);

            XYZ orig;
            if (ptHnd.HasValue)
               orig = new XYZ(origCoords[0], origCoords[1], newOffsetScaled);
            else
               orig = new XYZ(0.0, 0.0, newOffsetScaled);

            rampLocalPlacementHnds.Add(ExporterUtil.CreateLocalPlacement(file, newLevelHnd, orig, null, null));
         }

         IList<List<IFCAnyHandle>> newComponents = new List<List<IFCAnyHandle>>();
         for (int ii = 0; ii < numFlights - 1; ii++)
            newComponents.Add(new List<IFCAnyHandle>());

         int compIdx = 0;
         ElementId catId = CategoryUtil.GetSafeCategoryId(ramp);
         string predefType = ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4 ? "ShapeType" : "PredefinedType";

         foreach (IFCAnyHandle component in components)
         {
            string componentName = IFCAnyHandleUtil.GetStringAttribute(component, "Name");
            IFCAnyHandle componentProdRep = IFCAnyHandleUtil.GetInstanceAttribute(component, "Representation");

            IList<string> localComponentNames = new List<string>();
            IList<IFCAnyHandle> componentPlacementHnds = new List<IFCAnyHandle>();

            IFCAnyHandle localLocalPlacement = IFCAnyHandleUtil.GetObjectPlacement(component);
            IFCAnyHandle localRelativePlacement =
                (localLocalPlacement == null) ? null : IFCAnyHandleUtil.GetInstanceAttribute(localLocalPlacement, "RelativePlacement");

            bool isSubRamp = component.IsSubTypeOf(IFCEntityType.IfcRamp.ToString());
            for (int ii = 0; ii < numFlights - 1; ii++)
            {
               localComponentNames.Add((componentName == null) ? (ii + 2).ToString() : (componentName + ":" + (ii + 2)));
               if (isSubRamp)
                  componentPlacementHnds.Add(ExporterUtil.CopyLocalPlacement(file, rampLocalPlacementHnds[ii]));
               else
                  componentPlacementHnds.Add(IFCInstanceExporter.CreateLocalPlacement(file, rampLocalPlacementHnds[ii], localRelativePlacement));
            }

            IList<IFCAnyHandle> localComponentHnds = new List<IFCAnyHandle>();
            IList<IFCExportInfoPair> localCompExportInfo = new List<IFCExportInfoPair>();
            if (isSubRamp)
            {
               string componentType = IFCAnyHandleUtil.GetEnumerationAttribute(component, predefType);
               string localRampType = GetIFCRampType(componentType);
               
               for (int ii = 0; ii < numFlights - 1; ii++)
               {
                  string flightGUID = GUIDUtil.CreateSubElementGUID(ramp, ii + (int) IFCRampSubElements.FlightIdOffset);
                  IFCAnyHandle representationCopy =
                      ExporterUtil.CopyProductDefinitionShape(exporterIFC, ramp, catId, componentProdRep);

                  IFCAnyHandle localComponent = IFCInstanceExporter.CreateRamp(exporterIFC, ramp, flightGUID, ownerHistory,
                      componentPlacementHnds[ii], representationCopy, localRampType);

                  localComponentHnds.Add(localComponent);
                  IFCAnyHandleUtil.OverrideNameAttribute(localComponent, localComponentNames[ii]);
                  IFCExportInfoPair exportInfo = new IFCExportInfoPair(IFCEntityType.IfcRamp, localRampType);
                  localCompExportInfo.Add(exportInfo);
               }
            }
            else if (IFCAnyHandleUtil.IsSubTypeOf(component, IFCEntityType.IfcRampFlight))
            {
               for (int ii = 0; ii < numFlights - 1; ii++)
               {
                  string flightGUID = GUIDUtil.CreateSubElementGUID(ramp, ii + (int) IFCRampSubElements.FlightIdOffset);
                  IFCAnyHandle representationCopy =
                      ExporterUtil.CopyProductDefinitionShape(exporterIFC, ramp, catId, componentProdRep);

                  string rampFlightType = "NOTDEFINED";
                  IFCAnyHandle localComponent = IFCInstanceExporter.CreateRampFlight(exporterIFC, ramp, flightGUID, ownerHistory,
                      componentPlacementHnds[ii], representationCopy, rampFlightType);

                  IFCAnyHandleUtil.OverrideNameAttribute(localComponent, localComponentNames[ii]);
                  localComponentHnds.Add(localComponent);
                  IFCExportInfoPair exportInfo = new IFCExportInfoPair(IFCEntityType.IfcRampFlight, rampFlightType);
                  localCompExportInfo.Add(exportInfo);
               }
            }
            else if (IFCAnyHandleUtil.IsSubTypeOf(component, IFCEntityType.IfcSlab))
            {
               string componentType = IFCAnyHandleUtil.GetEnumerationAttribute(component, "PredefinedType");
               IFCSlabType localLandingType = FloorExporter.GetIFCSlabType(componentType);

               for (int ii = 0; ii < numFlights - 1; ii++)
               {
                  string landingGUID = GUIDUtil.CreateSubElementGUID(ramp, ii + (int)IFCRampSubElements.LandingIdOffset);
                  IFCAnyHandle representationCopy =
                      ExporterUtil.CopyProductDefinitionShape(exporterIFC, ramp, catId, componentProdRep);

                  IFCAnyHandle localComponent = IFCInstanceExporter.CreateSlab(exporterIFC, ramp, landingGUID, ownerHistory,
                      componentPlacementHnds[ii], representationCopy, localLandingType.ToString());
                  localComponentHnds.Add(localComponent);
                  IFCAnyHandleUtil.OverrideNameAttribute(localComponent, localComponentNames[ii]);
                  IFCExportInfoPair exportInfo = new IFCExportInfoPair(IFCEntityType.IfcSlab, localLandingType.ToString());
                  localCompExportInfo.Add(exportInfo);
               }
            }
            else if (IFCAnyHandleUtil.IsSubTypeOf(component, IFCEntityType.IfcMember))
            {
               for (int ii = 0; ii < numFlights - 1; ii++)
               {
                  string stringerGUID = GUIDUtil.CreateSubElementGUID(ramp, ii + (int)IFCRampSubElements.StringerIdOffset);
                  IFCAnyHandle representationCopy =
                      ExporterUtil.CopyProductDefinitionShape(exporterIFC, ramp, catId, componentProdRep);
                  string localMemberType = "STRINGER";

                  IFCAnyHandle localComponent = IFCInstanceExporter.CreateMember(exporterIFC, ramp, stringerGUID, ownerHistory,
                componentPlacementHnds[ii], representationCopy, localMemberType);
                  localComponentHnds.Add(localComponent);
                  IFCAnyHandleUtil.OverrideNameAttribute(localComponent, localComponentNames[ii]);
                  IFCExportInfoPair exportInfo = new IFCExportInfoPair(IFCEntityType.IfcMember, localMemberType);
                  localCompExportInfo.Add(exportInfo);
               }
            }

            for (int ii = 0; ii < numFlights - 1; ii++)
            {
               if (localComponentHnds[ii] != null)
               {
                  newComponents[ii].Add(localComponentHnds[ii]);
                  productWrapper.AddElement(null, localComponentHnds[ii], levelInfos[ii], componentECData[compIdx], false, localCompExportInfo[ii]);
               }
            }
            compIdx++;
         }

         // finally add a copy of the container.
         IList<IFCAnyHandle> rampCopyHnds = new List<IFCAnyHandle>();
         for (int ii = 0; ii < numFlights - 1; ii++)
         {
            string rampTypeAsString = IFCAnyHandleUtil.GetEnumerationAttribute(rampHnd, predefType);
            string rampType = GetIFCRampType(rampTypeAsString);
            
            string containerRampName = 
               IFCAnyHandleUtil.GetStringAttribute(rampHnd, "Name") + ":" + (ii + 2);
            string containerGuid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(ramp, containerRampName));
            IFCAnyHandle rampCopyHnd = IFCInstanceExporter.CreateRamp(exporterIFC, ramp,
               containerGuid, ownerHistory, rampLocalPlacementHnds[ii], null, rampType);

            rampCopyHnds.Add(rampCopyHnd);
            IFCAnyHandleUtil.OverrideNameAttribute(rampCopyHnd, containerRampName);
            IFCExportInfoPair exportInfo = new IFCExportInfoPair(IFCEntityType.IfcRamp, rampType);
            productWrapper.AddElement(ramp, rampCopyHnds[ii], levelInfos[ii], null, true, exportInfo);
         }

         for (int ii = 0; ii < numFlights - 1; ii++)
         {
            StairRampContainerInfo stairRampInfo = new StairRampContainerInfo(rampCopyHnds[ii], newComponents[ii],
                rampLocalPlacementHnds[ii]);
            ExporterCacheManager.StairRampContainerInfoCache.AppendStairRampContainerInfo(ramp.Id, stairRampInfo);
         }
      }

      /// <summary>
      /// Exports a ramp to IfcRamp, without decomposing into separate runs and landings.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="ifcEnumType">The ramp type.</param>
      /// <param name="ramp">The ramp element.</param>
      /// <param name="geometryElement">The geometry element.</param>
      /// <param name="numFlights">The number of flights for a multistory ramp.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void ExportRamp(ExporterIFC exporterIFC, string ifcEnumType, Element ramp, GeometryElement geometryElement,
          int numFlights, ProductWrapper productWrapper)
      {
         if (ramp == null || geometryElement == null)
            return;

         // Check the intended IFC entity or type name is in the exclude list specified in the UI
         IFCEntityType elementClassTypeEnum = IFCEntityType.IfcRamp;
         if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(elementClassTypeEnum))
            return;

         IFCFile file = exporterIFC.GetFile();
         ElementId categoryId = CategoryUtil.GetSafeCategoryId(ramp);

         Document doc = ramp.Document;
         ElementType rampType = doc.GetElement(ramp.GetTypeId()) as ElementType;

         using (IFCTransaction tr = new IFCTransaction(file))
         {
            using (PlacementSetter placementSetter = PlacementSetter.Create(exporterIFC, ramp, null))
            {
               IFCAnyHandle contextOfItemsFootPrint = ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.FootPrint);
               IFCAnyHandle contextOfItemsAxis = ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Axis);

               Transform trf = ExporterIFCUtils.GetUnscaledTransform(exporterIFC, placementSetter.LocalPlacement);
               IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

               string predefType = ifcEnumType;
               IFCExportInfoPair exportTypePair = ExporterUtil.GetProductExportType(ramp, out ifcEnumType);
               if (!exportTypePair.IsPredefinedTypeDefault)
               {
                  predefType = exportTypePair.PredefinedType;
               }

               SortedDictionary<double,IList<(Solid body, Face largestTopFace)>> rampFlights = null;
               SortedDictionary<double, IList<(Solid body, Face largestTopFace)>> landings = null;
               if (IdentifyRampFlightAndLanding(geometryElement, out rampFlights, out landings))
               {
                  string rampGUID = GUIDUtil.CreateGUID(ramp);
                  IFCAnyHandle rampLocalPlacement = placementSetter.LocalPlacement;

                  // Create appropriate type
                  IFCExportInfoPair exportType = CreateRampExportInfoPair(IFCEntityType.IfcRamp, IFCEntityType.IfcRampType, predefType);

                  // For IFC4 and Structural Exchange Requirement export, ramp container will be exported as IFCSlab type
                  IFCAnyHandle rampContainerHnd = IFCInstanceExporter.CreateGenericIFCEntity(exportType, file, ramp, rampGUID, ownerHistory,
                     rampLocalPlacement, null);

                  IFCAnyHandle rampTypeHnd = ExporterUtil.CreateGenericTypeFromElement(ramp, exportType, file, productWrapper);
                  ExporterCacheManager.TypeRelationsCache.Add(rampTypeHnd, rampContainerHnd);
                  productWrapper.AddElement(ramp, rampContainerHnd, placementSetter.LevelInfo, null, true, exportType);

                  //Breakdown the Ramp into its components: RampFlights and Landings
                  int rampFlightIndex = 0;
                  int landingIndex = 0;
                  HashSet<IFCAnyHandle> rampComponents = new ();
                  foreach (KeyValuePair<double,IList<(Solid body, Face topFace)>> rampFlight in rampFlights)
                  {
                     foreach ((Solid body, Face topFace) flightItem in rampFlight.Value)
                     {
                        using (IFCExportBodyParams ecData = new IFCExportBodyParams())
                        {
                           ecData.AllowVerticalOffsetOfBReps = false;
                           ecData.SetLocalPlacement(ExporterUtil.CreateLocalPlacement(file, placementSetter.LocalPlacement, null));
                           ecData.ReuseLocalPlacement = true;
                           BodyExporterOptions bodyExporterOptions = new (true, ExportOptionsCache.ExportTessellationLevel.ExtraLow);
                           BodyData bodyData = BodyExporter.ExportBody(exporterIFC, ramp, categoryId, ElementId.InvalidElementId, flightItem.body, bodyExporterOptions, ecData);

                           IFCAnyHandle bodyRep = bodyData.RepresentationHnd;
                           if (IFCAnyHandleUtil.IsNullOrHasNoValue(bodyRep))
                           {
                              ecData.ClearOpenings();
                              continue;
                           }
                           List<IFCAnyHandle> reps = [ bodyRep ];

                           Transform boundingBoxTrf = (bodyData.OffsetTransform == null) ? Transform.Identity : bodyData.OffsetTransform.Inverse;
                           IList<GeometryObject> solidList = [ flightItem.body ];
                           IFCAnyHandle boundingBoxRep = BoundingBoxExporter.ExportBoundingBox(exporterIFC, solidList, boundingBoxTrf);
                           if (boundingBoxRep != null)
                              reps.Add(boundingBoxRep);

                           IFCAnyHandle representation = IFCInstanceExporter.CreateProductDefinitionShape(exporterIFC.GetFile(), null, null, reps);

                           rampFlightIndex++;
                           string flightGUID = GUIDUtil.CreateSubElementGUID(ramp, rampFlightIndex + (int)IFCRampSubElements.FlightIdOffset);
                           string origFlightName = IFCAnyHandleUtil.GetStringAttribute(rampContainerHnd, "Name") + " " + rampFlightIndex;
                           string flightName = NamingUtil.GetOverrideStringValue(ramp, "IfcRampFlight.Name (" + rampFlightIndex + ")", origFlightName);

                           IFCAnyHandle flightLocalPlacement = ecData.GetLocalPlacement();
                           string flightPredefType = NamingUtil.GetOverrideStringValue(ramp, "IfcRampFlight.PredefinedType (" + rampFlightIndex + ")", null);
                           if (string.IsNullOrEmpty(flightPredefType))
                              flightPredefType = NamingUtil.GetOverrideStringValue(ramp, "IfcRampFlight.PredefinedType", null);

                           // Create appropriate type
                           IFCExportInfoPair flightExportType = CreateRampExportInfoPair(IFCEntityType.IfcRampFlight, IFCEntityType.IfcRampFlightType, flightPredefType);

                           // For IFC4 and Structural Exchange Requirement export, ramp container will be exported as IFCSlab type
                           IFCAnyHandle rampFlightHnd = IFCInstanceExporter.CreateGenericIFCEntity(flightExportType, file, null, flightGUID, ownerHistory,
                              flightLocalPlacement, representation);

                           IFCAnyHandleUtil.OverrideNameAttribute(rampFlightHnd, flightName);
                           rampComponents.Add(rampFlightHnd);

                           // Create type
                           string flightTypeGUID = GUIDUtil.CreateSubElementGUID(rampType, rampFlightIndex + (int)IFCRampSubElements.FlightIdOffset);
                           IFCAnyHandle flightTypeHnd = IFCInstanceExporter.CreateGenericIFCType(flightExportType, 
                              rampType, flightTypeGUID, exporterIFC.GetFile(), null, null);
                           IFCAnyHandleUtil.OverrideNameAttribute(flightTypeHnd, flightName);
                           ExporterCacheManager.TypeRelationsCache.Add(flightTypeHnd, rampFlightHnd);

                           CategoryUtil.CreateMaterialAssociation(exporterIFC, ramp, rampFlightHnd, bodyData.MaterialIds);

                           IFCAnyHandle psetRampFlightCommonHnd = CreatePSetRampFlightCommon(file, ramp, rampFlightIndex, flightItem.topFace);

                           if (!IFCAnyHandleUtil.IsNullOrHasNoValue(psetRampFlightCommonHnd))
                           {
                              HashSet<IFCAnyHandle> relatedObjects = new() { rampFlightHnd };
                              ExporterUtil.CreateRelDefinesByProperties(file, ownerHistory, null, null, relatedObjects, psetRampFlightCommonHnd);
                           }

                           CreateQuantitySetRampFlight(exporterIFC, file, rampFlightHnd, ramp, flightItem, rampFlightIndex);
                        }
                     }
                  }

                  foreach (KeyValuePair<double, IList<(Solid body, Face largestTopFace)>> landing in landings)
                  {
                     foreach ((Solid body, Face topFace) landingItem in landing.Value)
                     {
                        using (IFCExportBodyParams ecData = new ())
                        {
                           ecData.AllowVerticalOffsetOfBReps = false;
                           ecData.SetLocalPlacement(ExporterUtil.CreateLocalPlacement(file, placementSetter.LocalPlacement, null));
                           ecData.ReuseLocalPlacement = true;
                           BodyExporterOptions bodyExporterOptions = new (true, ExportOptionsCache.ExportTessellationLevel.ExtraLow);
                           BodyData bodyData = BodyExporter.ExportBody(exporterIFC, ramp, categoryId, ElementId.InvalidElementId, landingItem.body, bodyExporterOptions, ecData);

                           IFCAnyHandle bodyRep = bodyData.RepresentationHnd;
                           if (IFCAnyHandleUtil.IsNullOrHasNoValue(bodyRep))
                           {
                              ecData.ClearOpenings();
                              continue;
                           }
                           List<IFCAnyHandle> reps = [ bodyRep ];

                           Transform boundingBoxTrf = (bodyData.OffsetTransform == null) ? Transform.Identity : bodyData.OffsetTransform.Inverse;
                           IList<GeometryObject> solidList = [ landingItem.body ];
                           IFCAnyHandle boundingBoxRep = BoundingBoxExporter.ExportBoundingBox(exporterIFC, solidList, boundingBoxTrf);
                           if (boundingBoxRep != null)
                              reps.Add(boundingBoxRep);

                           IFCAnyHandle representation = IFCInstanceExporter.CreateProductDefinitionShape(exporterIFC.GetFile(), null, null, reps);

                           landingIndex++;
                           string landingGUID = GUIDUtil.CreateSubElementGUID(ramp, landingIndex + (int)IFCRampSubElements.LandingIdOffset);
                           string origLandingName = IFCAnyHandleUtil.GetStringAttribute(rampContainerHnd, "Name") + " " + landingIndex;
                           string landingName = NamingUtil.GetOverrideStringValue(ramp, "IfcRampLanding.Name (" + landingIndex + ")", origLandingName);

                           IFCAnyHandle landingLocalPlacement = ecData.GetLocalPlacement();
                           string landingPredefType = "LANDING";

                           IFCAnyHandle rampLandingHnd = IFCInstanceExporter.CreateSlab(exporterIFC, ramp, landingGUID, ownerHistory, landingLocalPlacement,
                               representation, landingPredefType);
                           IFCAnyHandleUtil.OverrideNameAttribute(rampLandingHnd, landingName);
                           rampComponents.Add(rampLandingHnd);

                           // Create type
                           IFCExportInfoPair landingExportType = new (IFCEntityType.IfcSlab, landingPredefType);
                           string landingTypeGUID = GUIDUtil.CreateSubElementGUID(rampType, landingIndex + (int)IFCRampSubElements.LandingIdOffset);
                           
                           IFCAnyHandle landingTypeHnd = IFCInstanceExporter.CreateGenericIFCType(landingExportType, 
                              rampType, landingTypeGUID, exporterIFC.GetFile(), null, null);
                           IFCAnyHandleUtil.OverrideNameAttribute(landingTypeHnd, landingName);
                           ExporterCacheManager.TypeRelationsCache.Add(landingTypeHnd, rampLandingHnd);

                           CategoryUtil.CreateMaterialAssociation(exporterIFC, ramp, rampLandingHnd, bodyData.MaterialIds);

                           IFCAnyHandle psetSlabCommonHnd = CreatePSetRampLandingCommon(file, ramp, landingIndex);

                           if (!IFCAnyHandleUtil.IsNullOrHasNoValue(psetSlabCommonHnd))
                           {
                              HashSet<IFCAnyHandle> relatedObjects = new() { rampLandingHnd };
                              ExporterUtil.CreateRelDefinesByProperties(file, ownerHistory, null, null, relatedObjects, psetSlabCommonHnd);
                           }

                           CreateQuantitySetLanding(exporterIFC, file, rampLandingHnd, ramp, landingItem, landingIndex);
                        }
                     }
                  }

                  if (rampComponents.Count > 0)
                  {
                     string relGuid = GUIDUtil.GenerateIFCGuidFrom(
                        GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAggregates, rampContainerHnd));
                     IFCInstanceExporter.CreateRelAggregates(file, relGuid, ownerHistory, null, null, rampContainerHnd, rampComponents);
                  }
               }
               else
               {
                  using (IFCExportBodyParams ecData = new())
                  {
                     ecData.SetLocalPlacement(placementSetter.LocalPlacement);
                     ecData.ReuseLocalPlacement = false;

                     var oneLevelGeom = GeometryUtil.GetOneLevelGeometryElement(geometryElement, numFlights);
                     GeometryElement rampGeom = oneLevelGeom.element;

                     BodyData bodyData;

                     BodyExporterOptions bodyExporterOptions = new(true, ExportOptionsCache.ExportTessellationLevel.ExtraLow);
                     IFCAnyHandle representation = RepresentationUtil.CreateAppropriateProductDefinitionShape(exporterIFC,
                         ramp, categoryId, rampGeom, bodyExporterOptions, null, ecData, out bodyData, instanceGeometry: true);

                     if (IFCAnyHandleUtil.IsNullOrHasNoValue(representation))
                     {
                        ecData.ClearOpenings();
                        return;
                     }

                     string containedRampGuid = GUIDUtil.CreateSubElementGUID(ramp, (int)IFCRampSubElements.ContainedRamp);
                     IFCAnyHandle containedRampLocalPlacement = ExporterUtil.CreateLocalPlacement(file, ecData.GetLocalPlacement(), null);
                     
                     if (numFlights == 1)
                     {
                        string guid = GUIDUtil.CreateGUID(ramp);
                        IFCAnyHandle localPlacement = ecData.GetLocalPlacement();

                        IFCAnyHandle rampHnd = IFCInstanceExporter.CreateRamp(exporterIFC, ramp, guid, ownerHistory,
                           localPlacement, representation, exportTypePair.GetPredefinedTypeOrDefault());
                        productWrapper.AddElement(ramp, rampHnd, placementSetter.LevelInfo, ecData, true, exportTypePair);
                        CategoryUtil.CreateMaterialAssociation(exporterIFC, ramp,rampHnd, bodyData.MaterialIds);

                        IFCAnyHandle rampTypeHnd = ExporterCacheManager.ElementTypeToHandleCache.Find(rampType, exportTypePair);
                        if (IFCAnyHandleUtil.IsNullOrHasNoValue(rampTypeHnd))
                        {
                           string typeGuid = GUIDUtil.CreateGUID(rampType);
                           rampTypeHnd = IFCInstanceExporter.CreateGenericIFCType(exportTypePair,
                              rampType, typeGuid, exporterIFC.GetFile(), null, null);
                           productWrapper.RegisterHandleWithElementType(rampType, exportTypePair, rampTypeHnd, null);
                        }
                        ExporterCacheManager.TypeRelationsCache.Add(rampTypeHnd, rampHnd);
                     }
                     else
                     {
                        List<IFCAnyHandle> components = [];
                        List<IFCExportBodyParams> componentExtrusionData = [];
                        IFCAnyHandle containedRampHnd = IFCInstanceExporter.CreateRamp(exporterIFC, ramp, containedRampGuid, ownerHistory,
                           containedRampLocalPlacement, representation, exportTypePair.GetPredefinedTypeOrDefault());
                        components.Add(containedRampHnd);
                        componentExtrusionData.Add(ecData);
                        
                        CategoryUtil.CreateMaterialAssociation(exporterIFC, ramp, containedRampHnd, bodyData.MaterialIds);

                        string guid = GUIDUtil.CreateGUID(ramp);
                        IFCAnyHandle localPlacement = ecData.GetLocalPlacement();

                        IFCAnyHandle rampHnd = IFCInstanceExporter.CreateRamp(exporterIFC, ramp, guid, ownerHistory,
                           localPlacement, null, exportTypePair.GetPredefinedTypeOrDefault());
                        productWrapper.AddElement(ramp, rampHnd, placementSetter.LevelInfo, ecData, true, exportTypePair);

                        string typeGuid = GUIDUtil.CreateGUID(rampType);
                        IFCAnyHandle rampTypeHnd = IFCInstanceExporter.CreateGenericIFCType(exportTypePair,
                           rampType, typeGuid, exporterIFC.GetFile(), null, null);
                        ExporterCacheManager.TypeRelationsCache.Add(rampTypeHnd, rampHnd);

                        StairRampContainerInfo stairRampInfo = new(rampHnd, components, localPlacement);
                        ExporterCacheManager.StairRampContainerInfoCache.AddStairRampContainerInfo(ramp.Id, stairRampInfo);

                        ExportMultistoryRamp(exporterIFC, ramp, numFlights, rampHnd, components, componentExtrusionData, placementSetter,
                            productWrapper);
                     }
                  }
               }
            }
            tr.Commit();
         }
      }

      /// <summary>
      /// Exports a ramp to IfcRamp.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="element">The ramp element.</param>
      /// <param name="geometryElement">The geometry element.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      public static void Export(ExporterIFC exporterIFC, Element element, GeometryElement geometryElement, ProductWrapper productWrapper)
      {
         IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(element, out _);
         IFCFile file = exporterIFC.GetFile();

         using (IFCTransaction tr = new IFCTransaction(file))
         {
            StairsExporter.ExportLegacyStairOrRampAsContainer(exporterIFC, exportType.GetPredefinedTypeOrDefault(), element, geometryElement, productWrapper);

            // If we didn't create a handle here, then the element wasn't a "native" Ramp, and is likely a FamilyInstance or a DirectShape.
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(productWrapper.GetAnElement()))
            {
               int numFlights = GetNumFlightsForRamp(element);
               if (numFlights > 0)
                  ExportRamp(exporterIFC, exportType.PredefinedType, element, geometryElement, numFlights, productWrapper);
            }

            tr.Commit();
         }
      }

      static bool IdentifyRampFlightAndLanding(GeometryElement rampGeom, out SortedDictionary<double,IList<(Solid body, Face largestTopFace)>> rampFlights, out SortedDictionary<double, IList<(Solid body, Face largestTopFace)>> landings)
      {
         rampFlights = new SortedDictionary<double, IList<(Solid body, Face largestTopFace)>>();
         landings = new SortedDictionary<double, IList<(Solid body, Face largestTopFace)>>();
         int totalComponents = 0;

         if (!ExporterCacheManager.ExportOptionsCache.ExportAs2x2)
         {
            SolidMeshGeometryInfo info = GeometryUtil.GetSplitSolidMeshGeometry(rampGeom, Transform.Identity);
            IList<Solid> solidList = info.GetSolids();
            foreach (Solid solid in solidList)
            {
               // Determine the largest face and with normal pointing to upward (+Z region). If the normal is exactly at +Z (0,0,1), then it should be landing
               Face rampComponentFace = GeometryUtil.GetLargestFaceInSolid(solid, new XYZ(0, 0, 1));
               if (rampComponentFace == null)
                  continue;

               // The solids will be sorted by their lowest Z position from the bounding box
               XYZ normal = rampComponentFace.ComputeNormal(new UV());
               BoundingBoxXYZ bBox = solid.GetBoundingBox();
               double lowestbbZ = bBox.Transform.OfPoint(bBox.Min).Z;
               if (MathUtil.IsAlmostEqual(normal.Z, 1.0))
               {
                  if (landings.ContainsKey(lowestbbZ))
                  {
                     landings[lowestbbZ].Add((solid, rampComponentFace));
                     totalComponents++;
                  }
                  else
                  {
                     IList<(Solid body, Face largestTopFace)> bodies = new List<(Solid body, Face largestTopFace)>() { (solid, rampComponentFace) };
                     landings.Add(lowestbbZ, bodies);
                     totalComponents++;
                  }
               }
               else
               {
                  if (rampFlights.ContainsKey(lowestbbZ))
                  {
                     rampFlights[lowestbbZ].Add((solid, rampComponentFace));
                     totalComponents++;
                  }
                  else
                  {
                     IList<(Solid body, Face largestTopFace)> bodies = new List<(Solid body, Face largestTopFace)>() { (solid, rampComponentFace) };
                     rampFlights.Add(lowestbbZ, bodies);
                     totalComponents++;
                  }
               }
            }
         }

         // Return false if there is no components identified, or if total is only one (a single geometry). For a single geometry, IfcRamp will be created with this geometry
         if ((rampFlights.Count == 0 && landings.Count == 0) || totalComponents == 1)
            return false;

         return true;
      }

      static IFCExportInfoPair CreateRampExportInfoPair(IFCEntityType entityName, IFCEntityType entityType, string predefType)
      {
         // For IFC4 and Structural Exchange Requirement export, ramps will be exported as IFCSlab type
         if (ExporterCacheManager.ExportOptionsCache.ExportAs4 && 
            (ExporterCacheManager.ExportOptionsCache.ExchangeRequirement == KnownERNames.Structural))
         {
            entityName = IFCEntityType.IfcSlab;
            entityType = IFCEntityType.IfcSlabType;
         }

         return new IFCExportInfoPair(entityName, entityType, predefType);
      }

      private static IFCAnyHandle CreatePSetRampFlightCommon(IFCFile file, Element element, int flightIndex, Face topFace)
      {
         HashSet<IFCAnyHandle> properties = new HashSet<IFCAnyHandle>();

         string paramSetName = "Pset_RampFlightCommon";

         AddStringValueToPropertySet(file, element, properties, paramSetName, "Reference", flightIndex,
            IFCDataUtil.CreateAsIdentifier);

         AddDoubleValueToPropertySet(file, element, properties, paramSetName, "HeadRoom", flightIndex,
            UnitUtil.ScaleLength, IFCDataUtil.CreateAsPositiveLengthMeasure);

         // Slope
         double slope = 0.0;
         if (topFace != null)
         {
            XYZ faceNormal = topFace.ComputeNormal(new UV());
            XYZ projectionToXYPlane = new XYZ(faceNormal.X, faceNormal.Y, 0);
            slope = GeometryUtil.GetAngleOfFace(topFace, projectionToXYPlane);
         }

         // The property set for components is determined by index in the parameter name, but if it does not exist, it will check a common one without index 
         double doubleParamOverride = 0.0;
         if (GetDoubleValueFromElement(element, "Pset_RampFlightCommon.Slope (" + flightIndex.ToString() + ")", out doubleParamOverride) != null
            || GetDoubleValueFromElement(element, "Pset_RampFlightCommon.Slope", out doubleParamOverride) != null)
         {
            slope = doubleParamOverride;
         }

         // Slope
         if (!MathUtil.IsAlmostZero(slope))
         {
            IFCData paramVal = IFCDataUtil.CreateAsPlaneAngleMeasure(slope);
            PropertyDescription propertyDescription = new PropertyDescription("Slope");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }

         if (!ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         {
            string baseParamName = "Status";
            string baseFullParamName = paramSetName + "." + baseParamName;
            Parameter param = GetStringValueFromElement(element, baseFullParamName + " (" + flightIndex.ToString() + ")", out string stringParam);
            if (param == null)
               param = GetStringValueFromElement(element, baseFullParamName, out stringParam);
            if (param != null)
            {
               PropertyDescription propertyDescription = new PropertyDescription(baseParamName);
               IFCAnyHandle propSingleValue = PropertyUtil.CreateLabelPropertyFromCache(file, param.Id, propertyDescription, stringParam, PropertyValueType.EnumeratedValue,
                     true, typeof(PropertySet.IFC4.PEnum_ElementStatus));
          
               if (propSingleValue != null)
                  properties.Add(propSingleValue);
            }

            AddDoubleValueToPropertySet(file, element, properties, paramSetName, "ClearWidth", flightIndex,
               UnitUtil.ScaleLength, IFCDataUtil.CreateAsPositiveLengthMeasure);

            double doubleParam = 0.0;
            if (!MathUtil.IsAlmostZero(slope))
               doubleParam = UnitUtil.ScaleAngle(Math.PI / 2.0) - slope;
            if (GetDoubleValueFromElement(element, "Pset_RampFlightCommon.CounterSlope (" + flightIndex.ToString() + ")", out doubleParamOverride) != null
               || GetDoubleValueFromElement(element, "Pset_RampFlightCommon.CounterSlope", out doubleParamOverride) != null)
            {
               doubleParam = doubleParamOverride;
            }

            if (!MathUtil.IsAlmostZero(doubleParam))
            {
               IFCData paramVal = IFCDataUtil.CreateAsPlaneAngleMeasure(doubleParam);
               PropertyDescription propertyDescription = new PropertyDescription("CounterSlope");
               IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
               properties.Add(propSingleValue);
            }
         }

         if (properties.Count > 0)
         {
            string guid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(element, paramSetName + ": " + flightIndex.ToString()));
            return IFCInstanceExporter.CreatePropertySet(file,
               guid, ExporterCacheManager.OwnerHistoryHandle, paramSetName, null, properties);
         }

         return null;
      }

      private static void AddStringValueToPropertySet(IFCFile file, Element element, 
         HashSet<IFCAnyHandle> properties, string psetName, string propertyName, int index, 
         Func<string, IFCData> dataFn)
      {
         if (GetStringValueFromElement(element, psetName + "." + propertyName + " (" + index.ToString() + ")", out string param) == null)
            GetStringValueFromElement(element, psetName + "." + propertyName, out param);
         {
            IFCData paramVal = dataFn(param);
            PropertyDescription propertyDescription = new PropertyDescription(propertyName);
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }
      }

      private static void AddDoubleValueToPropertySet(IFCFile file, Element element,
         HashSet<IFCAnyHandle> properties, string psetName, string propertyName, int index,
         Func<double, double> scalar, Func<double, IFCData> dataFn)
      {
         if (GetDoubleValueFromElement(element, psetName + "." + propertyName + " (" + index.ToString() + ")", out double doubleParam) == null)
            GetDoubleValueFromElement(element, psetName + "." + propertyName, out doubleParam);
         {
            if (scalar != null)
               doubleParam = scalar(doubleParam);
            IFCData paramVal = dataFn(doubleParam);
            PropertyDescription propertyDescription = new PropertyDescription(propertyName);
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }
      }

      private static void AddBoolValueToPropertySet(IFCFile file, Element element,
         HashSet<IFCAnyHandle> properties, string psetName, string propertyName, int index,
         Func<bool, IFCData> dataFn)
      {
         if (GetIntValueFromElement(element, psetName + "." + propertyName + " (" + index.ToString() + ")", out int intParam) == null)
            GetIntValueFromElement(element, psetName + "." + propertyName, out intParam);
         {
            IFCData paramVal = dataFn(intParam != 0);
            PropertyDescription propertyDescription = new PropertyDescription(propertyName);
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }
      }


      private static IFCAnyHandle CreatePSetRampLandingCommon(IFCFile file, Element element, int landingIndex)
      {
         HashSet<IFCAnyHandle> properties = new HashSet<IFCAnyHandle>();

         string psetName = "Pset_SlabCommon";
         AddStringValueToPropertySet(file, element, properties, psetName, "Reference", landingIndex,
            IFCDataUtil.CreateAsIdentifier);
         AddStringValueToPropertySet(file, element, properties, psetName, "AcousticRating", landingIndex,
            IFCDataUtil.CreateAsLabel);
         AddStringValueToPropertySet(file, element, properties, psetName, "FireRating", landingIndex,
            IFCDataUtil.CreateAsLabel);
         AddStringValueToPropertySet(file, element, properties, psetName, "SurfaceSpreadOfFlame", landingIndex,
            IFCDataUtil.CreateAsLabel);

         // Skip PitchAngle, it does not write the property as it should be 0 (a criteria for Landing)

         AddDoubleValueToPropertySet(file, element, properties, psetName, "ThermalTransmittance", landingIndex,
            UnitUtil.ScaleThermalTransmittance, IFCDataUtil.CreateAsThermalTransmittanceMeasure);
         
         AddBoolValueToPropertySet(file, element, properties, psetName, "Combustible", landingIndex,
            IFCDataUtil.CreateAsBoolean);
         AddBoolValueToPropertySet(file, element, properties, psetName, "Compartmentation", landingIndex,
            IFCDataUtil.CreateAsBoolean);
         AddBoolValueToPropertySet(file, element, properties, psetName, "IsExternal", landingIndex,
            IFCDataUtil.CreateAsBoolean);
         AddBoolValueToPropertySet(file, element, properties, psetName, "LoadBearing", landingIndex,
            IFCDataUtil.CreateAsBoolean);

         if (!ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         {
            string propertyName = "Status";
            string baseParamName = psetName + "." + propertyName;
            Parameter param = GetStringValueFromElement(element, baseParamName + " (" + landingIndex.ToString() + ")", out string stringParam);
            if (param == null)
               param = GetStringValueFromElement(element, baseParamName, out stringParam);
            if (param != null)
            {
               PropertyDescription propertyDescription = new PropertyDescription(propertyName);
               IFCAnyHandle propSingleValue = PropertyUtil.CreateLabelPropertyFromCache(file, param.Id, propertyDescription,
                  stringParam, PropertyValueType.EnumeratedValue,
                  true, typeof(PropertySet.IFC4.PEnum_ElementStatus));

               if (propSingleValue != null)
                  properties.Add(propSingleValue);
            }
         }

         if (properties.Count > 0)
         {
            string guid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(element, 
               "Landing " + psetName + ": " + landingIndex.ToString()));
            return IFCInstanceExporter.CreatePropertySet(file, guid,
               ExporterCacheManager.OwnerHistoryHandle, psetName, null, properties);
         }

         return null;
      }

      private static void CreateQuantitySetRampFlight(ExporterIFC exporterIFC, IFCFile file, IFCAnyHandle rampFlightHnd, Element element, (Solid body, Face topFace) geometry, int flightIndex)
      {
         HashSet<IFCAnyHandle> quantityHnds = new HashSet<IFCAnyHandle>();
         double area = geometry.topFace.Area;
         if (!MathUtil.IsAlmostZero(area))
         {
            area = UnitUtil.ScaleArea(area);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityArea(file, "NetArea", null, null, area);
            quantityHnds.Add(quantityHnd);
         }

         double volume = geometry.body.Volume;
         if (!MathUtil.IsAlmostZero(volume))
         {
            volume = UnitUtil.ScaleVolume(volume);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityVolume(file, "NetVolume", null, null, volume);
            quantityHnds.Add(quantityHnd);
         }

         // For the rest of quantities, we cannot determine the quantities for freeform RampFlight and therefore it will rely on parameters
         double doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampFlight.IfcQtyLength (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampFlight.IfcQtyLength", out doubleParam) != null)
         {
            doubleParam = UnitUtil.ScaleLength(doubleParam);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "Length", null, null, doubleParam);
            quantityHnds.Add(quantityHnd); 
         }

         doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampFlight.IfcQtyWidth (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampFlight.IfcQtyWidth", out doubleParam) != null)
         {
            doubleParam = UnitUtil.ScaleLength(doubleParam);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "Width", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampFlight.IfcQtyGrossArea (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampFlight.IfcQtyGrossArea", out doubleParam) != null)
         {
            doubleParam = UnitUtil.ScaleArea(doubleParam);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "GrossArea", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampFlight.IfcQtyGrossVolume (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampFlight.IfcQtyGrossVolume", out doubleParam) != null)
         {
            doubleParam = UnitUtil.ScaleVolume(doubleParam);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "GrossVolume", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         string quantitySetName = string.Empty;
         if (!ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         {
            quantitySetName = "Qto_RampFlightBaseQuantities";
         }

         PropertyUtil.CreateAndRelateBaseQuantities(file, exporterIFC, rampFlightHnd, quantityHnds, quantitySetName);
      }

      private static void CreateQuantitySetLanding(ExporterIFC exporterIFC, IFCFile file, IFCAnyHandle rampLandingHnd, Element element, (Solid body, Face topFace) geometry, int flightIndex)
      {
         HashSet<IFCAnyHandle> quantityHnds = new HashSet<IFCAnyHandle>();
         double area = geometry.topFace.Area;
         if (!MathUtil.IsAlmostZero(area))
         {
            area = UnitUtil.ScaleArea(area);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityArea(file, "NetArea", null, null, area);
            quantityHnds.Add(quantityHnd);
         }

         double volume = geometry.body.Volume;
         if (!MathUtil.IsAlmostZero(volume))
         {
            volume = UnitUtil.ScaleVolume(volume);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityVolume(file, "NetVolume", null, null, volume);
            quantityHnds.Add(quantityHnd);
         }

         IList<CurveLoop> curveLoops = geometry.topFace.GetEdgesAsCurveLoops();
         double perimeter = curveLoops[0].GetExactLength();
         if (!MathUtil.IsAlmostZero(perimeter))
         {
            perimeter = UnitUtil.ScaleLength(perimeter);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "Perimeter", null, null, perimeter);
            quantityHnds.Add(quantityHnd);
         }

         // For the rest of quantities, we cannot determine the quantities for freeform Landing and therefore it will rely on parameters
         double doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyLength (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyLength", out doubleParam) != null)
         {
            doubleParam = UnitUtil.ScaleLength(doubleParam);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "Length", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyWidth (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyWidth", out doubleParam) != null)
         {
            doubleParam = UnitUtil.ScaleLength(doubleParam);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "Width", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyDepth (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyDepth", out doubleParam) != null)
         {
            doubleParam = UnitUtil.ScaleLength(doubleParam);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "Depth", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyGrossArea (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyGrossArea", out doubleParam) != null)
         {
            doubleParam = UnitUtil.ScaleArea(doubleParam);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "GrossArea", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyGrossVolume (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyGrossVolume", out doubleParam) != null)
         {
            doubleParam = UnitUtil.ScaleVolume(doubleParam);
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "GrossVolume", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyGrossWeight (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyGrossWeight", out doubleParam) != null)
         {
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "GrossWeight", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         doubleParam = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyNetWeight (" + flightIndex.ToString() + ")", out doubleParam) != null
            || ParameterUtil.GetDoubleValueFromElement(element, "IfcRampLanding.IfcQtyNetWeight", out doubleParam) != null)
         {
            IFCAnyHandle quantityHnd = IFCInstanceExporter.CreateQuantityLength(file, "NetWeight", null, null, doubleParam);
            quantityHnds.Add(quantityHnd);
         }

         string quantitySetName = string.Empty;
         if (!ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         {
            quantitySetName = "Qto_SlabBaseQuantities";
         }

         PropertyUtil.CreateAndRelateBaseQuantities(file, exporterIFC, rampLandingHnd, quantityHnds, quantitySetName);
      }

   }
}