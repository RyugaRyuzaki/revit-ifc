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
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Export.Utility;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Export.Exporter.PropertySet;
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;
using System.Linq;
using static Revit.IFC.Export.Utility.ParameterUtil;

namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// Provides methods to export spatial elements.
   /// </summary>
   class SpatialElementExporter
   {
      /// <summary>
      /// The SpatialElementGeometryCalculator object that contains some useful results from previous calculator.
      /// </summary>
      private static SpatialElementGeometryCalculator s_SpatialElementGeometryCalculator = null;

      /// <summary>
      /// Initializes SpatialElementGeometryCalculator object.
      /// </summary>
      /// <param name="document">
      /// The Revit document.
      /// </param>
      /// <param name="exporterIFC">
      /// The ExporterIFC object.
      /// </param>
      public static void InitializeSpatialElementGeometryCalculator(Document document)
      {
         SpatialElementBoundaryOptions options = GetSpatialElementBoundaryOptions(null);
         s_SpatialElementGeometryCalculator = new SpatialElementGeometryCalculator(document, options);
      }

      /// <summary>
      /// Destroys SpatialElementGeometryCalculator object.
      /// </summary>
      public static void DestroySpatialElementGeometryCalculator()
      {
         if (s_SpatialElementGeometryCalculator != null)
         {
            s_SpatialElementGeometryCalculator.Dispose();
            s_SpatialElementGeometryCalculator = null;
         }
      }

      /// <summary>
      /// Exports spatial elements, including rooms, areas and spaces. 1st level space boundaries.
      /// </summary>
      /// <param name="exporterIFC">
      /// The ExporterIFC object.
      /// </param>
      /// <param name="spatialElement">
      /// The spatial element.
      /// </param>
      /// <param name="productWrapper">
      /// The ProductWrapper.
      /// </param>
      public static void ExportSpatialElement(ExporterIFC exporterIFC, SpatialElement spatialElement, ProductWrapper productWrapper)
      {
         string ifcEnumType;
         IFCExportInfoPair exportInfo = ExporterUtil.GetProductExportType(spatialElement, out ifcEnumType);

         // Force the default export to IfcSpace for Spatial Element if it is set to UnKnown
         if (exportInfo.IsUnKnown)
         {
            exportInfo.SetByTypeAndPredefinedType(IFCEntityType.IfcSpace, ifcEnumType);
         }

         // Check the intended IFC entity or type name is in the exclude list specified in the UI
         if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(exportInfo.ExportInstance))
            return;

         IFCFile file = exporterIFC.GetFile();
         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            using (PlacementSetter setter = PlacementSetter.Create(exporterIFC, spatialElement, null, null))
            {
               SpatialElementGeometryResults spatialElemGeomResult = null;
               IFCAnyHandle spaceHnd = CreateIFCSpace(exporterIFC, spatialElement,
                  productWrapper, setter, exportInfo, out spatialElemGeomResult);
               if (IFCAnyHandleUtil.IsNullOrHasNoValue(spaceHnd))
               {
                  return;
               }

               // Do not create boundary information for areas.
               if (!(spatialElement is Area) && 
                  (ExporterCacheManager.ExportOptionsCache.SpaceBoundaryLevel == 1))
               {
                  Document document = spatialElement.Document;
                  ElementId levelId = spatialElement.LevelId;
                  IFCLevelInfo levelInfo = ExporterCacheManager.LevelInfoCache.GetLevelInfo(levelId);
                  double baseHeightNonScaled = spatialElement.Level?.Elevation ?? 0.0;

                  try
                  {
                     // This can throw an exception.  If it does, continue to export element without boundary information.
                     // We will re-use the previously generated value, if we have it.
                     // TODO: warn user.
                     if (spatialElemGeomResult == null)
                        spatialElemGeomResult = s_SpatialElementGeometryCalculator.CalculateSpatialElementGeometry(spatialElement);

                     Solid spatialElemGeomSolid = spatialElemGeomResult.GetGeometry();
                     FaceArray faces = spatialElemGeomSolid.Faces;
                     foreach (Face face in faces)
                     {
                        IList<SpatialElementBoundarySubface> spatialElemBoundarySubfaces = spatialElemGeomResult.GetBoundaryFaceInfo(face);
                        foreach (SpatialElementBoundarySubface spatialElemBSubface in spatialElemBoundarySubfaces)
                        {
                           if (spatialElemBSubface.SubfaceType == SubfaceType.Side)
                              continue;

                           if (spatialElemBSubface.GetSubface() == null)
                              continue;

                           ElementId elemId = spatialElemBSubface.SpatialBoundaryElement.LinkInstanceId;
                           if (elemId == ElementId.InvalidElementId)
                           {
                              elemId = spatialElemBSubface.SpatialBoundaryElement.HostElementId;
                           }

                           Element boundingElement = document.GetElement(elemId);
                           if (boundingElement == null)
                              continue;

                           IFCInternalOrExternal internalOrExternal = IFCInternalOrExternal.NotDefined;
                           if (CategoryUtil.IsElementExternal(boundingElement).HasValue)
                              internalOrExternal = CategoryUtil.IsElementExternal(boundingElement).Value ? IFCInternalOrExternal.External : IFCInternalOrExternal.Internal;

                           IFCGeometryInfo info = IFCGeometryInfo.CreateSurfaceGeometryInfo(spatialElement.Document.Application.VertexTolerance);

                           Face subFace = spatialElemBSubface.GetSubface();
                           ExporterIFCUtils.CollectGeometryInfo(exporterIFC, info, subFace, XYZ.Zero, false);

                           foreach (IFCAnyHandle surfaceHnd in info.GetSurfaces())
                           {
                              IFCAnyHandle connectionGeometry = IFCInstanceExporter.CreateConnectionSurfaceGeometry(file, 
                                 surfaceHnd, null);

                              SpaceBoundary spaceBoundary = new SpaceBoundary(null, 
                                 spatialElement.Id, 
                                 boundingElement.Id, setter.LevelId, 
                                 connectionGeometry, 
                                 IFCPhysicalOrVirtual.Physical,
                                 internalOrExternal);

                              if (!ProcessIFCSpaceBoundary(exporterIFC, spaceBoundary, file))
                                 ExporterCacheManager.SpaceBoundaryCache.Add(spaceBoundary);
                           }
                        }
                     }
                  }
                  catch
                  {
                  }

                  IList<IList<BoundarySegment>> roomBoundaries = spatialElement.GetBoundarySegments(GetSpatialElementBoundaryOptions(spatialElement));
                  double scaledRoomHeight = GetScaledHeight(spatialElement, levelId, levelInfo);
                  double unscaledHeight = UnitUtil.UnscaleLength(scaledRoomHeight);

                  Transform lcs = Transform.Identity;

                  foreach (IList<BoundarySegment> roomBoundaryList in roomBoundaries)
                  {
                     foreach (BoundarySegment roomBoundary in roomBoundaryList)
                     {
                        Element boundingElement = document.GetElement(roomBoundary.ElementId);

                        if (boundingElement == null)
                           continue;

                        ElementId boundingElementId = boundingElement.Id;
                        Curve trimmedCurve = roomBoundary.GetCurve();

                        if (trimmedCurve == null)
                           continue;

                        IFCAnyHandle connectionGeometry = ExtrusionExporter.CreateConnectionSurfaceGeometry(
                           exporterIFC, trimmedCurve, lcs, scaledRoomHeight, baseHeightNonScaled);

                        IFCPhysicalOrVirtual physOrVirt = IFCPhysicalOrVirtual.Physical;
                        if (boundingElement is CurveElement)
                           physOrVirt = IFCPhysicalOrVirtual.Virtual;
                        else if (boundingElement is Autodesk.Revit.DB.Architecture.Room)
                           physOrVirt = IFCPhysicalOrVirtual.NotDefined;

                        IFCInternalOrExternal internalOrExternal = IFCInternalOrExternal.NotDefined;
                        if (CategoryUtil.IsElementExternal(boundingElement).HasValue)
                           internalOrExternal = CategoryUtil.IsElementExternal(boundingElement).Value ? IFCInternalOrExternal.External : IFCInternalOrExternal.Internal;

                        bool isObjectPhys = (physOrVirt == IFCPhysicalOrVirtual.Physical);

                        SpaceBoundary spaceBoundary = new SpaceBoundary(null, 
                           spatialElement.Id,
                           boundingElement.Id,
                           setter.LevelId, 
                           !IFCAnyHandleUtil.IsNullOrHasNoValue(connectionGeometry) ? connectionGeometry : null,
                           physOrVirt,
                           internalOrExternal);

                        if (!ProcessIFCSpaceBoundary(exporterIFC, spaceBoundary, file))
                           ExporterCacheManager.SpaceBoundaryCache.Add(spaceBoundary);

                        // try to add doors and windows for host objects if appropriate.
                        if (isObjectPhys && boundingElement is HostObject)
                        {
                           HostObject hostObj = boundingElement as HostObject;
                           
                           HashSet<ElementId> elemIds = new HashSet<ElementId>();
                           elemIds.UnionWith(hostObj.FindInserts(false, false, false, false));
                           if (elemIds.Count == 0)
                           {
                              CurtainGridSet curtainGridSet = CurtainSystemExporter.GetCurtainGridSet(hostObj);
                              if (curtainGridSet != null)
                              {
                                 foreach (CurtainGrid curtainGrid in curtainGridSet)
                                 {
                                    elemIds.UnionWith(CurtainSystemExporter.GetVisiblePanelsForGrid(
                                       curtainGrid, document));
                                 }
                              }
                           }

                           foreach (ElementId elemId in elemIds)
                           {
                              // we are going to do a simple bbox export, not complicated geometry.
                              Element instElem = document.GetElement(elemId);
                              if (instElem == null)
                                 continue;

                              BoundingBoxXYZ instBBox = instElem.get_BoundingBox(null);
                              if (instBBox == null)
                                 continue;

                              // make copy of original trimmed curve.
                              Curve instCurve = trimmedCurve.Clone();
                              XYZ instOrig = instCurve.GetEndPoint(0);

                              // make sure that the insert is on this level.
                              if (instBBox.Max.Z < instOrig.Z)
                                 continue;
                              if (instBBox.Min.Z > instOrig.Z + unscaledHeight)
                                 continue;

                              double insHeight = Math.Min(instBBox.Max.Z, instOrig.Z + unscaledHeight) - Math.Max(instOrig.Z, instBBox.Min.Z);
                              if (insHeight < (1.0 / (12.0 * 16.0)))
                                 continue;

                              // move base curve to bottom of bbox.
                              XYZ moveDir = new XYZ(0.0, 0.0, instBBox.Min.Z - instOrig.Z);
                              Transform moveTrf = Transform.CreateTranslation(moveDir);
                              instCurve = instCurve.CreateTransformed(moveTrf);

                              bool isHorizOrVert = false;
                              if (instCurve is Line)
                              {
                                 Line instLine = instCurve as Line;
                                 XYZ lineDir = instLine.Direction;
                                 if (MathUtil.IsAlmostEqual(Math.Abs(lineDir.X), 1.0) || (MathUtil.IsAlmostEqual(Math.Abs(lineDir.Y), 1.0)))
                                    isHorizOrVert = true;
                              }

                              double[] parameters = new double[2];
                              double[] origEndParams = new double[2];
                              bool paramsSet = false;

                              if (!isHorizOrVert)
                              {
                                 FamilyInstance famInst = instElem as FamilyInstance;
                                 if (famInst == null)
                                    continue;

                                 ElementType elementType = document.GetElement(famInst.GetTypeId()) as ElementType;
                                 if (elementType == null)
                                    continue;

                                 BoundingBoxXYZ symBBox = elementType.get_BoundingBox(null);
                                 if (symBBox != null)
                                 {
                                    Curve symCurve = trimmedCurve.Clone();
                                    Transform trf = famInst.GetTransform();
                                    Transform invTrf = trf.Inverse;
                                    Curve trfCurve = symCurve.CreateTransformed(invTrf);
                                    parameters[0] = trfCurve.Project(symBBox.Min).Parameter;
                                    parameters[1] = trfCurve.Project(symBBox.Max).Parameter;
                                    paramsSet = true;
                                 }
                              }

                              if (!paramsSet)
                              {
                                 parameters[0] = instCurve.Project(instBBox.Min).Parameter;
                                 parameters[1] = instCurve.Project(instBBox.Max).Parameter;
                              }

                              // ignore if less than 1/16".
                              if (Math.Abs(parameters[1] - parameters[0]) < 1.0 / (12.0 * 16.0))
                                 continue;
                              if (parameters[0] > parameters[1])
                              {
                                 //swap
                                 double tempParam = parameters[0];
                                 parameters[0] = parameters[1];
                                 parameters[1] = tempParam;
                              }

                              origEndParams[0] = instCurve.GetEndParameter(0);
                              origEndParams[1] = instCurve.GetEndParameter(1);

                              if (origEndParams[0] > parameters[1] - (1.0 / (12.0 * 16.0)))
                                 continue;
                              if (origEndParams[1] < parameters[0] + (1.0 / (12.0 * 16.0)))
                                 continue;

                              instCurve.MakeBound(parameters[0] > origEndParams[0] ? parameters[0] : origEndParams[0],
                                                  parameters[1] < origEndParams[1] ? parameters[1] : origEndParams[1]);

                              double insHeightScaled = UnitUtil.ScaleLength(insHeight);
                              IFCAnyHandle insConnectionGeom = ExtrusionExporter.CreateConnectionSurfaceGeometry(exporterIFC, instCurve, lcs,
                                 insHeightScaled, baseHeightNonScaled);

                              SpaceBoundary instBoundary = new SpaceBoundary(null, 
                                 spatialElement.Id, elemId, setter.LevelId, 
                                 !IFCAnyHandleUtil.IsNullOrHasNoValue(insConnectionGeom) ? insConnectionGeom : null, 
                                 physOrVirt,
                                 internalOrExternal);
                              if (!ProcessIFCSpaceBoundary(exporterIFC, instBoundary, file))
                                 ExporterCacheManager.SpaceBoundaryCache.Add(instBoundary);
                           }
                        }
                     }
                  }
               }
            }
            transaction.Commit();
         }
      }

      /// <summary>
      /// Exports spatial elements, including rooms, areas and spaces. 2nd level space boundaries.
      /// </summary>
      /// <param name="ifcExporter">The Exporter object.</param>
      /// <param name="exporterIFC"> The ExporterIFC object.</param>
      /// <param name="document">The Revit document.</param>
      /// <returns>The set of exported spaces.  This is used to try to export using the standard routine for spaces that failed.</returns>
      public static ISet<ElementId> ExportSpatialElement2ndLevel(Exporter ifcExporter, ExporterIFC exporterIFC, Document document)
      {
         ISet<ElementId> exportedSpaceIds = new HashSet<ElementId>();

         using (SubTransaction st = new SubTransaction(document))
         {
            st.Start();

            EnergyAnalysisDetailModel model = null;
            try
            {
               View filterView = ExporterCacheManager.ExportOptionsCache.FilterViewForExport;
               IFCFile file = exporterIFC.GetFile();
               using (IFCTransaction transaction = new IFCTransaction(file))
               {

                  EnergyAnalysisDetailModelOptions options = new EnergyAnalysisDetailModelOptions();
                  options.Tier = EnergyAnalysisDetailModelTier.SecondLevelBoundaries; //2nd level space boundaries
                  options.SimplifyCurtainSystems = true;
                  try
                  {
                     model = EnergyAnalysisDetailModel.Create(document, options);
                  }
                  catch (System.Exception)
                  {
                     return exportedSpaceIds;
                  }

                  IList<EnergyAnalysisSpace> spaces = model.GetAnalyticalSpaces();
                  foreach (EnergyAnalysisSpace space in spaces)
                  {
                     SpatialElement spatialElement = document.GetElement(space.CADObjectUniqueId) as SpatialElement;

                     if (spatialElement == null)
                        continue;

                     //current view only
                     if (!ElementFilteringUtil.IsElementVisible(spatialElement))
                        continue;

                     if (!ElementFilteringUtil.ShouldElementBeExported(spatialElement, false))
                        continue;

                     if (ElementFilteringUtil.IsRoomInInvalidPhase(spatialElement))
                        continue;

                     Options geomOptions = GeometryUtil.GetIFCExportGeometryOptions();
                     View ownerView = spatialElement.Document.GetElement(spatialElement.OwnerViewId) as View;
                     if (ownerView != null)
                        geomOptions.View = ownerView;
                     GeometryElement geomElem = spatialElement.get_Geometry(geomOptions);

                     try
                     {
                        exporterIFC.PushExportState(spatialElement, geomElem);

                        using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
                        {
                           using (PlacementSetter setter = PlacementSetter.Create(exporterIFC, spatialElement))
                           {
                              // We won't use the SpatialElementGeometryResults, as these are 1st level boundaries, not 2nd level.
                              SpatialElementGeometryResults results = null;
                              IFCAnyHandle spaceHnd = CreateIFCSpace(exporterIFC,
                                 spatialElement, productWrapper, setter, null, out results);
                              if (IFCAnyHandleUtil.IsNullOrHasNoValue(spaceHnd))
                              {
                                 continue;
                              }

                              exportedSpaceIds.Add(spatialElement.Id);

                              XYZ offset = GetSpaceBoundaryOffset(setter);

                              //get boundary information from surfaces
                              IList<EnergyAnalysisSurface> surfaces = space.GetAnalyticalSurfaces();
                              foreach (EnergyAnalysisSurface surface in surfaces)
                              {
                                 Element boundingElement = GetBoundaryElement(document, surface.CADLinkUniqueId, surface.CADObjectUniqueId);

                                 IList<EnergyAnalysisOpening> openings = surface.GetAnalyticalOpenings();
                                 IFCAnyHandle connectionGeometry = CreateConnectionSurfaceGeometry(exporterIFC, surface, openings, offset);
                                 string name = surface.Name;
                                 CreateIFCSpaceBoundary(file, exporterIFC, name, spatialElement, 
                                    boundingElement, setter.LevelId, connectionGeometry);

                                 // try to add doors and windows for host objects if appropriate.
                                 if (boundingElement is HostObject)
                                 {
                                    foreach (EnergyAnalysisOpening opening in openings)
                                    {
                                       Element openingBoundingElem = GetBoundaryElement(document, opening.CADLinkUniqueId, opening.CADObjectUniqueId);
                                       IFCAnyHandle openingConnectionGeom = CreateConnectionSurfaceGeometry(exporterIFC, opening, offset);
                                       CreateIFCSpaceBoundary(file, exporterIFC, opening.OpeningName,
                                          spatialElement, openingBoundingElem, setter.LevelId, openingConnectionGeom);
                                    }
                                 }
                              }

                              ExporterUtil.ExportRelatedProperties(exporterIFC, spatialElement, productWrapper);
                           }
                        }
                     }
                     catch (Exception ex)
                     {
                        ifcExporter.HandleUnexpectedException(ex, spatialElement);
                     }
                     finally
                     {
                        exporterIFC.PopExportState();
                     }
                  }
                  transaction.Commit();
               }
            }
            finally
            {
               if (model != null)
                  document.Delete(model.Id);
            }

            st.RollBack();
            return exportedSpaceIds;
         }
      }

      /// <summary>
      /// Creates SpaceBoundary from a bounding element.
      /// </summary>
      /// <param name="file">The IFC file.</param>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="name">The optional unique name of the space boundary.</param>
      /// <param name="spatialElement">The spatial element.</param>
      /// <param name="boundingElement">The bounding element.</param>
      /// <param name="levelId">The level id.</param>
      /// <param name="connectionGeometry">The connection geometry handle.</param>
      static void CreateIFCSpaceBoundary(IFCFile file, ExporterIFC exporterIFC, string name,
         SpatialElement spatialElement, Element boundingElement, ElementId levelId,
         IFCAnyHandle connectionGeometry)
      {
         IFCPhysicalOrVirtual physOrVirt = IFCPhysicalOrVirtual.Physical;
         if (boundingElement == null || boundingElement is CurveElement)
            physOrVirt = IFCPhysicalOrVirtual.Virtual;
         else if (boundingElement is Autodesk.Revit.DB.Architecture.Room)
            physOrVirt = IFCPhysicalOrVirtual.NotDefined;

         IFCInternalOrExternal internalOrExternal = IFCInternalOrExternal.NotDefined;
         if (CategoryUtil.IsElementExternal(boundingElement).HasValue)
            internalOrExternal = CategoryUtil.IsElementExternal(boundingElement).Value ? IFCInternalOrExternal.External : IFCInternalOrExternal.Internal;

         SpaceBoundary spaceBoundary = new SpaceBoundary(name, spatialElement.Id,
            boundingElement?.Id ?? ElementId.InvalidElementId,
            levelId, connectionGeometry, physOrVirt,
            internalOrExternal);

         if (!ProcessIFCSpaceBoundary(exporterIFC, spaceBoundary, file))
            ExporterCacheManager.SpaceBoundaryCache.Add(spaceBoundary);
      }

      /// <summary>
      /// Gets element from a CAD Link's UniqueId and a CAD Object's UniqueId.
      /// </summary>
      /// <param name="document">
      /// The Revit document.
      /// </param>
      /// <param name="CADLinkUniqueId">
      /// The CAD Link's unique id.
      /// </param>
      /// <param name="CADObjectUniqueId">
      /// The CAD Object's unique id.
      /// </param>
      /// <returns>
      /// The element.
      /// </returns>
      static Element GetBoundaryElement(Document document, string CADLinkUniqueId, string CADObjectUniqueId)
      {
         Document documentToUse = document;
         if (!String.IsNullOrEmpty(CADLinkUniqueId))
         {
            RevitLinkInstance CADLinkInstance = document.GetElement(CADLinkUniqueId) as RevitLinkInstance;
            if (CADLinkInstance != null)
            {
               documentToUse = CADLinkInstance.GetLinkDocument();
            }
         }

         return documentToUse.GetElement(CADObjectUniqueId);
      }

      /// <summary>
      /// Gets the boundary options of a spatial element.
      /// </summary>
      /// <param name="spatialElement">The spatial element. null to get the default options.</param>
      /// <returns>The SpatialElementBoundaryOptions.</returns>
      static SpatialElementBoundaryOptions GetSpatialElementBoundaryOptions(SpatialElement spatialElement)
      {
         SpatialElementBoundaryOptions spatialElementBoundaryOptions = new SpatialElementBoundaryOptions();
         spatialElementBoundaryOptions.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;

         if (spatialElement == null)
            return spatialElementBoundaryOptions;

         SpatialElementType spatialElementType = SpatialElementType.Room;
         if (spatialElement is Autodesk.Revit.DB.Architecture.Room)
         {
            spatialElementType = SpatialElementType.Room;
         }
         else if (spatialElement is Area)
         {
            spatialElementType = SpatialElementType.Area;
         }
         else if (spatialElement is Autodesk.Revit.DB.Mechanical.Space)
         {
            spatialElementType = SpatialElementType.Space;
         }
         else
            return spatialElementBoundaryOptions;

         AreaVolumeSettings areaSettings = AreaVolumeSettings.GetAreaVolumeSettings(spatialElement.Document);
         if (areaSettings != null)
         {
            spatialElementBoundaryOptions.SpatialElementBoundaryLocation = areaSettings.GetSpatialElementBoundaryLocation(spatialElementType);
         }

         return spatialElementBoundaryOptions;
      }

      /// <summary>Creates IFC connection surface geometry from a surface object.</summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="surface">The EnergyAnalysisSurface.</param>
      /// <param name="openings">List of EnergyAnalysisOpenings.</param>
      /// <param name="offset">The offset of the geometry.</param>
      /// <returns>The connection geometry handle.</returns>
      static IFCAnyHandle CreateConnectionSurfaceGeometry(ExporterIFC exporterIFC, EnergyAnalysisSurface surface, IList<EnergyAnalysisOpening> openings, XYZ offset)
      {
         IFCFile file = exporterIFC.GetFile();

         Polyloop outerLoop = surface.GetPolyloops().FirstOrDefault();
         IList<XYZ> outerLoopPoints = outerLoop.GetPoints();

         IList<XYZ> newOuterLoopPoints = new List<XYZ>();
         foreach (XYZ point in outerLoopPoints)
         {
            newOuterLoopPoints.Add(UnitUtil.ScaleLength(point.Subtract(offset)));
         }

         IList<IList<XYZ>> innerLoopPoints = new List<IList<XYZ>>();
         foreach (EnergyAnalysisOpening opening in openings)
         {
            IList<XYZ> openingPoints = opening.GetPolyloops().FirstOrDefault().GetPoints();
            List<XYZ> newOpeningPoints = new List<XYZ>();
            foreach (XYZ openingPoint in openingPoints)
            {
               newOpeningPoints.Add(UnitUtil.ScaleLength(openingPoint.Subtract(offset)));
            }
            innerLoopPoints.Add(newOpeningPoints);
         }

         IFCAnyHandle hnd = ExporterUtil.CreateCurveBoundedPlane(file, newOuterLoopPoints, innerLoopPoints);

         return IFCInstanceExporter.CreateConnectionSurfaceGeometry(file, hnd, null);
      }

      /// <summary>
      /// Creates IFC connection surface geometry from an opening object.
      /// </summary>
      /// <param name="file">The IFC file.</param>
      /// <param name="opening">The EnergyAnalysisOpening.</param>
      /// <param name="offset">The offset of opening.</param>
      /// <returns>The connection surface geometry handle.</returns>
      static IFCAnyHandle CreateConnectionSurfaceGeometry(ExporterIFC exporterIFC, EnergyAnalysisOpening opening, XYZ offset)
      {
         IFCFile file = exporterIFC.GetFile();

         Polyloop outerLoop = opening.GetPolyloops().FirstOrDefault();
         IList<XYZ> outerLoopPoints = outerLoop.GetPoints();

         List<XYZ> newOuterLoopPoints = new List<XYZ>();
         foreach (XYZ point in outerLoopPoints)
         {
            newOuterLoopPoints.Add(UnitUtil.ScaleLength(point.Subtract(offset)));
         }

         IList<IList<XYZ>> innerLoopPoints = new List<IList<XYZ>>();

         IFCAnyHandle hnd = ExporterUtil.CreateCurveBoundedPlane(file, newOuterLoopPoints, innerLoopPoints);

         return IFCInstanceExporter.CreateConnectionSurfaceGeometry(file, hnd, null);
      }

      /// <summary>
      /// Gets the height of a spatial element.
      /// </summary>
      /// <param name="spatialElement">The spatial element.</param>
      /// <param name="levelId">The level id.</param>
      /// <param name="levelInfo">The level info.</param>
      /// <returns>The height, scaled in IFC units.</returns>
      static double GetScaledHeight(SpatialElement spatialElement, ElementId levelId, IFCLevelInfo levelInfo)
      {
         Document document = spatialElement.Document;
         
         ElementId topLevelId = ElementId.InvalidElementId;
         double topOffset = 0.0;

         // These values are internally set for areas, but are invalid.  Ignore them and just use the level height.
         if (!(spatialElement is Area))
         {
            ParameterUtil.GetElementIdValueFromElement(spatialElement, BuiltInParameter.ROOM_UPPER_LEVEL, out topLevelId);
            ParameterUtil.GetDoubleValueFromElement(spatialElement, BuiltInParameter.ROOM_UPPER_OFFSET, out topOffset);
         }

         double bottomOffset;
         ParameterUtil.GetDoubleValueFromElement(spatialElement, BuiltInParameter.ROOM_LOWER_OFFSET, out bottomOffset);

         Level bottomLevel = document.GetElement(levelId) as Level;
         Level topLevel =
            (levelId == topLevelId) ? bottomLevel : document.GetElement(topLevelId) as Level;

         double roomHeight = 0.0;
         if (bottomLevel != null && topLevel != null)
         {
            roomHeight = (topLevel.Elevation - bottomLevel.Elevation) + (topOffset - bottomOffset);
            roomHeight = UnitUtil.ScaleLength(roomHeight);
         }

         if (MathUtil.IsAlmostZero(roomHeight))
         {
            double levelHeight = ExporterCacheManager.LevelInfoCache.FindHeight(levelId);
            if (levelHeight < 0.0)
               levelHeight = LevelUtil.CalculateDistanceToNextLevel(document, levelId, levelInfo);

            roomHeight = UnitUtil.ScaleLength(levelHeight);
         }

         // For area spaces, we assign a dummy height (1 unit), as we are not allowed to export IfcSpaces without a volumetric representation.
         if (MathUtil.IsAlmostZero(roomHeight) && spatialElement is Area)
         {
            roomHeight = 1.0;
         }

         return roomHeight;
      }

      /// <summary>
      /// Gets the offset of the space boundary.
      /// </summary>
      /// <param name="setter">The placement settter.</param>
      /// <returns>The offset.</returns>
      static XYZ GetSpaceBoundaryOffset(PlacementSetter setter)
      {
         IFCAnyHandle localPlacement = setter.LocalPlacement;
         double zOffset = setter.Offset;

         IFCAnyHandle relPlacement = GeometryUtil.GetRelativePlacementFromLocalPlacement(localPlacement);
         if (!IFCAnyHandleUtil.IsNullOrHasNoValue(relPlacement))
         {
            IFCAnyHandle ptHnd = IFCAnyHandleUtil.GetLocation(relPlacement);
            if (!IFCAnyHandleUtil.IsNullOrHasNoValue(ptHnd))
            {
               IList<double> addToCoords = IFCAnyHandleUtil.GetCoordinates(ptHnd);
               return new XYZ(addToCoords[0], addToCoords[1], addToCoords[2] + zOffset);
            }
         }

         return new XYZ(0, 0, zOffset);
      }

      /// <summary>
      /// Creates space boundary.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="boundary">The space boundary object.</param>
      /// <param name="file">The IFC file.</param>
      /// <returns>True if processed successfully, false otherwise.</returns>
      public static bool ProcessIFCSpaceBoundary(ExporterIFC exporterIFC, SpaceBoundary boundary,
         IFCFile file)
      {
         string ifcSpaceBoundaryName = string.Empty;
         if (ExporterCacheManager.ExportOptionsCache.SpaceBoundaryLevel == 1)
            ifcSpaceBoundaryName = "1stLevel";
         else if (ExporterCacheManager.ExportOptionsCache.SpaceBoundaryLevel == 2)
            ifcSpaceBoundaryName = "2ndLevel";

         IFCAnyHandle spatialElemHnd = ExporterCacheManager.SpaceInfoCache.FindSpaceHandle(boundary.SpatialElementId);
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(spatialElemHnd))
            return false;

         IFCPhysicalOrVirtual boundaryType = boundary.SpaceBoundaryType;
         IFCAnyHandle buildingElemHnd = null;
         if (boundaryType == IFCPhysicalOrVirtual.Physical)
         {
            buildingElemHnd = exporterIFC.FindSpaceBoundingElementHandle(boundary.NonSpatialElementId, boundary.LevelId);
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(buildingElemHnd))
               return false;
         }

         string hashCode = boundary.GetBoundaryHashCode();
         string guid = GUIDUtil.GenerateIFCGuidFrom(
            GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelSpaceBoundary, hashCode));

         IFCInstanceExporter.CreateRelSpaceBoundary(file, guid,
            ExporterCacheManager.OwnerHistoryHandle, ifcSpaceBoundaryName, null,
            spatialElemHnd, buildingElemHnd, boundary.ConnectionGeometryHandle, boundaryType,
            boundary.InternalOrExternal);

         return true;
      }

      /// <summary>
      /// Creates COBIESpaceClassifications.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC.</param>
      /// <param name="file">The file.</param>
      /// <param name="spaceHnd">The space handle.</param>
      /// <param name="spatialElement">The spatial element.</param>
      private static void CreateCOBIESpaceClassifications(IFCFile file, IFCAnyHandle spaceHnd,
         SpatialElement spatialElement)
      {
         string bimStandardsLocation = ExporterCacheManager.ClassificationCache.GetBIMStandardsURL(spatialElement);
         
         // OCCS - Space by Function.
         if (ParameterUtil.GetStringValueFromElement(spatialElement, "OmniClass Number", out string itemReference) != null)
         {
            string itemName;
            ParameterUtil.GetStringValueFromElement(spatialElement, "OmniClass Title", out itemName);

            IFCAnyHandle classification;
            if (!ExporterCacheManager.ClassificationCache.ClassificationHandles.TryGetValue("OmniClass", out classification))
            {
               classification = IFCInstanceExporter.CreateClassification(file, "CSI (Construction Specifications Institute)", "v 1.0", 0, 0, 0, 
                  "OmniClass", "OmniClass Classification", "http://www.omniclass.org");
               ExporterCacheManager.ClassificationCache.ClassificationHandles.Add("OmniClass", classification);
            }

            string guid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(spatialElement, "IfcRelAssociatesClassification: OmniClass"));
            ClassificationReferenceKey key = new ClassificationReferenceKey(
               "http://www.omniclass.org/tables/OmniClass_13_2006-03-28.pdf", itemReference,
               itemName, null, classification);
            ExporterCacheManager.ClassificationCache.AddRelation(file, key, guid, null,
               spaceHnd);
         }

         // Space Type (Owner)
         CreateOneCOBIEClassificationReference(file, spatialElement, spaceHnd, null, 
            "Space Type (Owner)", bimStandardsLocation);

         // Space Category (Owner)
         CreateOneCOBIEClassificationReference(file, spatialElement, spaceHnd, null,
            "Space Category (Owner)", bimStandardsLocation);

         // Space Category (BOMA)
         CreateOneCOBIEClassificationReference(file, spatialElement, spaceHnd, null,
            "Space Category (BOMA)", "http://www.BOMA.org");
      }

      static void AddAreaToAreaScheme(Element spatialElement, IFCAnyHandle spaceHnd)
      {
         Element areaScheme = (spatialElement as Area)?.AreaScheme;

         if (areaScheme != null)
         {
            ElementId areaSchemeId = areaScheme.Id;
            HashSet<IFCAnyHandle> areas = null;
            if (!ExporterCacheManager.AreaSchemeCache.TryGetValue(areaSchemeId, out areas))
            {
               areas = new HashSet<IFCAnyHandle>();
               ExporterCacheManager.AreaSchemeCache[areaSchemeId] = areas;
            }
            areas.Add(spaceHnd);
         }
      }

      /// <summary>
      /// Creates IFC room/space/area item, not include boundaries. 
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="spatialElement">The spatial element.</param>
      /// <param name="productWrapper">The ProductWrapper.</param>
      /// <param name="setter">The PlacementSetter.</param>
      /// <returns>Returns the created handle, or null.</returns>
      static IFCAnyHandle CreateIFCSpace(ExporterIFC exporterIFC, SpatialElement spatialElement, ProductWrapper productWrapper,
          PlacementSetter setter, IFCExportInfoPair exportInfo, out SpatialElementGeometryResults results)
      {
         results = null;

         if (exportInfo == null)
            exportInfo = new IFCExportInfoPair(IFCEntityType.IfcSpace);

         // Avoid throwing for a spatial element with no location.
         if (spatialElement.Location == null)
            return null;

         IList<CurveLoop> curveLoops = null;
         GeometryElement geomElem = null;

         Area spatialElementAsArea = spatialElement as Area;

         // Advanced Option Use2DRoomBoundaryForRoomVolumeCreation --> No Geometry Element used.
         if (!ExporterCacheManager.ExportOptionsCache.Use2DRoomBoundaryForRoomVolumeCreation)
         {
            geomElem = (spatialElement as Autodesk.Revit.DB.Architecture.Room)?.ClosedShell;
            if (geomElem == null)
            {
               geomElem = (spatialElement as Autodesk.Revit.DB.Mechanical.Space)?.ClosedShell;
               if (geomElem == null && spatialElementAsArea != null)
               {
                  Options geomOptions = GeometryUtil.GetIFCExportGeometryOptions();
                  geomElem = spatialElementAsArea.get_Geometry(geomOptions);
               }
            }
         }

         // If the override container parameter is detected, the LevelInfo and LocalPlacement will be overridden
         IFCAnyHandle overrideContainer;
         ElementId overrideContainerId = ParameterUtil.OverrideContainmentParameter(spatialElement, out overrideContainer);

         ElementId levelId = spatialElement.LevelId;
         IFCLevelInfo levelInfo = ExporterCacheManager.LevelInfoCache.GetLevelInfo(levelId);

         if (!IFCAnyHandleUtil.IsNullOrHasNoValue(overrideContainer))
         {
            if (overrideContainerId != ElementId.InvalidElementId)
            {
               levelInfo = ExporterCacheManager.LevelInfoCache.GetLevelInfo(overrideContainerId);
               levelId = overrideContainerId;
            }
            else
            {
               levelInfo = null;    //Reset LevelInfo, since the override container does not have an associated element
            }
         }

         double scaledRoomHeight = GetScaledHeight(spatialElement, levelId, levelInfo);
         if (scaledRoomHeight <= 0.0)
            return null;

         double dArea = 0.0;  // Will be calculated later.
         IFCFile file = exporterIFC.GetFile();

         IFCAnyHandle spaceHnd = null;
         using (IFCExportBodyParams extraParams = new IFCExportBodyParams())
         {
            IFCAnyHandle localPlacement = setter.LocalPlacement;

            extraParams.SetLocalPlacement(localPlacement);
            extraParams.PossibleExtrusionAxes = IFCExtrusionAxes.TryZ;

            using (IFCTransaction transaction2 = new IFCTransaction(file))
            {
               ElementId catId = CategoryUtil.GetSafeCategoryId(spatialElement);

               // 'SweptSolid' requires CurveLoops for its 'Body' geometry.
               // 'BRep' requires CurveLoops for mandatory 'Footprint' geometry only.
               IFCAnyHandle repHnd = null;
               SpatialElementBoundaryOptions options = GetSpatialElementBoundaryOptions(spatialElement);

               try
               {
                  curveLoops = ExporterIFCUtils.GetRoomBoundaryAsCurveLoopArray(spatialElement, options, true);
                  if (curveLoops == null || curveLoops.Count == 0)
                     return null;
               }
               catch
               {
                  return null;
               }

               double bottomOffset;
               ParameterUtil.GetDoubleValueFromElement(spatialElement, BuiltInParameter.ROOM_LOWER_OFFSET, out bottomOffset);

               double elevation = spatialElement.Level?.Elevation ?? 0.0;
               XYZ orig = new XYZ(0, 0, elevation + bottomOffset);
               Transform lcs = Transform.CreateTranslation(orig); // room calculated as level offset.

               // Attempt 1:  'SweptSolid' IfcShapeRepresentation (with a 'BoudingBox').
               // For Use2DRoomBoundaryForRoomVolumeCreation no Bounding Box will be created.
               IFCAnyHandle shapeRep = ExtrusionExporter.CreateExtrudedSolidFromCurveLoop(exporterIFC, null, curveLoops, lcs, XYZ.BasisZ, scaledRoomHeight, true, out _);
               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(shapeRep))
               {
                  // Spaces shouldn't have styled items.
                  HashSet<IFCAnyHandle> bodyItems = new HashSet<IFCAnyHandle>() { shapeRep };

                  IFCAnyHandle contextOfItems = ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Body);
                  shapeRep = RepresentationUtil.CreateSweptSolidRep(exporterIFC, spatialElement, catId, contextOfItems, bodyItems, null, null);
                  IList<IFCAnyHandle> shapeReps = new List<IFCAnyHandle>() { shapeRep };

                  IFCAnyHandle boundingBoxRep = BoundingBoxExporter.ExportBoundingBox(exporterIFC, geomElem, Transform.Identity);
                  if (boundingBoxRep != null)
                     shapeReps.Add(boundingBoxRep);

                  repHnd = IFCInstanceExporter.CreateProductDefinitionShape(file, null, null, shapeReps);
               }

               // Attempt 2:  'BRep' IfcShapeRepresentation and 'Footprint' IfcShapeRepresentation.
               // This will not run for Use2DRoomBoundaryForRoomVolumeCreation advanced option.
               if ((repHnd == null) && (geomElem != null))
               {
                  // Attempt to create Body Geometry for IfcSpace.
                  BodyData bodyData;
                  BodyExporterOptions bodyExporterOptions = new BodyExporterOptions(true, ExportOptionsCache.ExportTessellationLevel.Medium);
                  repHnd = RepresentationUtil.CreateAppropriateProductDefinitionShape(exporterIFC, spatialElement,
                      catId, geomElem, bodyExporterOptions, null, extraParams, out bodyData, false);

                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(repHnd))
                  {
                     if (bodyData.RepresentationHnd == null)
                     {
                        repHnd = null;
                     }
                     else if (bodyData.ShapeRepresentationType == ShapeRepresentationType.Brep)
                     {
                        IList<IFCAnyHandle> representations = new List<IFCAnyHandle>();
                        FootPrintInfo fInfo = new FootPrintInfo(curveLoops, lcs);
                        IFCAnyHandle footprintShapeRep = fInfo.CreateFootprintShapeRepresentation(exporterIFC);
                        representations.Add(footprintShapeRep);
                        IFCAnyHandleUtil.AddRepresentations(repHnd, representations);
                     }
                  }
               }

               // Attempt 3 (final fallback): Try 'SweptSolid' using the CurveLoops, forcing the Use2DRoomBoundaryForRoomVolumeCreation option.
               // If the Use2DRoomBoundaryForRoomVolumeCreation was already set, this was already done for attempt 1, so there is no need to  do it again here.
               // Also the CurveLoops were used to build the FootPrint, so this should be equivalent to using any 'FootPrint' alternate representation.
               if ((repHnd == null) && !ExporterCacheManager.ExportOptionsCache.Use2DRoomBoundaryForRoomVolumeCreation)
               {
                  shapeRep = ExtrusionExporter.CreateExtrudedSolidFromCurveLoop(exporterIFC, null, curveLoops, lcs, XYZ.BasisZ, scaledRoomHeight, true, out _);
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(shapeRep))
                     return null;

                  // Spaces shouldn't have styled items.
                  HashSet<IFCAnyHandle> bodyItems = new HashSet<IFCAnyHandle>() { shapeRep };

                  IFCAnyHandle contextOfItems = ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Body);
                  shapeRep = RepresentationUtil.CreateSweptSolidRep(exporterIFC, spatialElement, catId, contextOfItems, bodyItems, null, null);
                  IList<IFCAnyHandle> shapeReps = new List<IFCAnyHandle>() { shapeRep };

                  repHnd = IFCInstanceExporter.CreateProductDefinitionShape(file, null, null, shapeReps);
               }

               if (ParameterUtil.GetDoubleValueFromElement(spatialElement, BuiltInParameter.ROOM_AREA, out dArea) != null)
               dArea = UnitUtil.ScaleArea(dArea);

               extraParams.ScaledHeight = scaledRoomHeight;
               extraParams.ScaledArea = dArea;

               if (exportInfo.ExportInstance == IFCEntityType.IfcSpace)
               {
                  IFCInternalOrExternal internalOrExternal = IFCInternalOrExternal.NotDefined;
                  if(CategoryUtil.IsElementExternal(spatialElement).HasValue)
                     internalOrExternal = CategoryUtil.IsElementExternal(spatialElement).Value ? IFCInternalOrExternal.External : IFCInternalOrExternal.Internal;
                  
                  string preDefinedType = exportInfo.GetPredefinedTypeOrDefault();

                  spaceHnd = IFCInstanceExporter.CreateSpace(exporterIFC, spatialElement, 
                     GUIDUtil.CreateGUID(spatialElement), ExporterCacheManager.OwnerHistoryHandle,
                     extraParams.GetLocalPlacement(), repHnd, IFCElementComposition.Element,
                     internalOrExternal, preDefinedType);
               }
               else
               {
                  spaceHnd = IFCInstanceExporter.CreateGenericIFCEntity(exportInfo, file, 
                     spatialElement, GUIDUtil.CreateGUID(spatialElement), 
                     ExporterCacheManager.OwnerHistoryHandle, extraParams.GetLocalPlacement(), repHnd);
               }

               if (exportInfo.ExportType != IFCEntityType.UnKnown)
               {
                  IFCAnyHandle type = ExporterUtil.CreateGenericTypeFromElement(spatialElement, exportInfo, file, productWrapper);
                  ExporterCacheManager.TypeRelationsCache.Add(type, spaceHnd);
               }
               transaction2.Commit();
            }

            if (spaceHnd != null)
            {
               productWrapper.AddSpace(spatialElement, spaceHnd, levelInfo, extraParams, true, exportInfo);
               AddAreaToAreaScheme(spatialElement, spaceHnd);
            }
         }

         // Save room handle for later use/relationships
         ExporterCacheManager.SpaceInfoCache.SetSpaceHandle(spatialElement, spaceHnd);

         // Find Ceiling as a Space boundary and keep the relationship in a cache for use later
         GetCeilingSpaceBoundary(spatialElement, out results);

         if (!MathUtil.IsAlmostZero(dArea))
         {
            // TODO: Determine if we even need this for IFC2x2, or just IFC2x3.  This is a workaround for the pre-2010 GSA requirements, that don't have their own MVD.
            bool mvdSupportDesignGrossArea = ExporterCacheManager.ExportOptionsCache.ExportAs2x2 || ExporterCacheManager.ExportOptionsCache.ExportAs2x3CoordinationView1;
            bool addonMVDSupportDesignGrossArea = !ExporterCacheManager.ExportIFCBaseQuantities();
            if (mvdSupportDesignGrossArea && addonMVDSupportDesignGrossArea)
            {
               string strSpaceNumber = null;
               if (ParameterUtil.GetStringValueFromElement(spatialElement, BuiltInParameter.ROOM_NUMBER, out strSpaceNumber) == null)
                  strSpaceNumber = null;

               string spatialElementName = NamingUtil.GetNameOverride(spatialElement, strSpaceNumber);

               bool isDesignGrossArea = (string.Compare(spatialElementName, "GSA Design Gross Area") > 0);
               PropertyUtil.CreatePreCOBIEGSAQuantities(exporterIFC, spaceHnd, "GSA Space Areas", (isDesignGrossArea ? "GSA Design Gross Area" : "GSA BIM Area"), dArea);
            }
         }

         // Export Classifications for SpatialElement for GSA/COBIE.
         if (ExporterCacheManager.ExportOptionsCache.ExportAsCOBIE)
            CreateCOBIESpaceClassifications(file, spaceHnd, spatialElement);

         return spaceHnd;
      }

      /// <summary>
      /// Collect relationship information from Ceiling to Room to be used later to determine whether a Ceiling can be contained in a Room
      /// </summary>
      /// <param name="spatialElement">The revit spatial object to process</param>
      /// <param name="results">The results of the CalculateSpatialElementGeometry call, for caching for later use.</param>
      /// <returns>True if it found a ceiling, false otherwise.</returns>
      static private bool GetCeilingSpaceBoundary(SpatialElement spatialElement, out SpatialElementGeometryResults results)
      {
         results = null;

         // Areas don't have a 3D rep, so no ceiling space boundaries.
         if (spatialElement is Area)
            return false;

         // Represents the criteria for boundary elements to be considered bounding Ceiling
         LogicalOrFilter categoryFilter = new LogicalOrFilter(new ElementCategoryFilter(BuiltInCategory.OST_Ceilings),
                                                     new ElementCategoryFilter(BuiltInCategory.OST_Ceilings));

         try
         {
            if (s_SpatialElementGeometryCalculator == null)
               return false;
            results = s_SpatialElementGeometryCalculator.CalculateSpatialElementGeometry(spatialElement);
         }
         catch
         {
            return false;
         }

         Solid geometry = results.GetGeometry();

         // Go through the boundary faces to identify whether it is bounded by a Ceiling. If it is Ceiling, add into the Cache
         foreach (Face face in geometry.Faces)
         {
            IList<SpatialElementBoundarySubface> boundaryFaces = results.GetBoundaryFaceInfo(face);
            foreach (SpatialElementBoundarySubface boundaryFace in boundaryFaces)
            {
               // Get boundary element
               LinkElementId boundaryElementId = boundaryFace.SpatialBoundaryElement;

               // Only considering local file room bounding elements
               ElementId localElementId = boundaryElementId.HostElementId;
               // Evaluate if element meets criteria using PassesFilter()
               if (localElementId != ElementId.InvalidElementId && categoryFilter.PassesFilter(spatialElement.Document, localElementId))
               {
                  if (ExporterCacheManager.CeilingSpaceRelCache.ContainsKey(localElementId))
                  {
                     // The ceiling already exists in the Dictionary, add the Space into list
                     IList<ElementId> roomlist = ExporterCacheManager.CeilingSpaceRelCache[localElementId];
                     roomlist.Add(spatialElement.Id);
                  }
                  else
                  {
                     // The first time this Ceiling Id appears
                     IList<ElementId> roomlist = new List<ElementId>();
                     roomlist.Add(spatialElement.Id);
                     ExporterCacheManager.CeilingSpaceRelCache.Add(localElementId, roomlist);
                  }

               }
            }
         }

         return true;
      }

      /// <summary>
      /// Creates spatial zone energy analysis property set.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC.</param>
      /// <param name="file">The file.</param>
      /// <param name="element">The element.</param>
      /// <returns>The handle.</returns>
      static private IFCAnyHandle CreateSpatialZoneEnergyAnalysisPSet(IFCFile file, Element element)
      {
         // Property Sets.  We don't use the generic Property Set mechanism because Zones aren't "real" elements.
         HashSet<IFCAnyHandle> properties = new HashSet<IFCAnyHandle>();

         string paramValue = "";
         if (GetStringValueFromElement(element, "Spatial Zone Conditioning Requirement", out paramValue) != null)
         {
            IFCData paramVal = IFCDataUtil.CreateAsLabel(paramValue);
            PropertyDescription propertyDescription = new PropertyDescription("SpatialZoneConditioningRequirement");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file,
               propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }

         if (ParameterUtil.GetStringValueFromElement(element, "HVAC System Type", out paramValue) != null)
         {
            IFCData paramVal = IFCDataUtil.CreateAsLabel(paramValue);
            PropertyDescription propertyDescription = new PropertyDescription("HVACSystemType");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }

         if (GetStringValueFromElement(element, "User Defined HVAC System Type", out paramValue) != null)
         {
            IFCData paramVal = IFCDataUtil.CreateAsLabel(paramValue);
            PropertyDescription propertyDescription = new PropertyDescription("UserDefinedHVACSystemType");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file,
               propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }

         double infiltrationRate = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "Infiltration Rate", out infiltrationRate) != null)
         {
            IFCData paramVal = IFCDataUtil.CreateAsReal(infiltrationRate);
            IFCAnyHandle unitHnd = !ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView ? ExporterCacheManager.UnitsCache.FindUserDefinedUnit("ACH") : null;
            PropertyDescription propertyDescription = new PropertyDescription("InfiltrationRate");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, unitHnd);
            properties.Add(propSingleValue);
         }

         int isDaylitZone = 0;
         if (GetIntValueFromElement(element, "Is Daylit Zone", out isDaylitZone) != null)
         {
            IFCData paramVal = IFCDataUtil.CreateAsBoolean(isDaylitZone != 0);
            PropertyDescription propertyDescription = new PropertyDescription("IsDaylitZone");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }

         int numberOfDaylightSensors = 0;
         if (ParameterUtil.GetIntValueFromElement(element, "Number of Daylight Sensors", out numberOfDaylightSensors) != null)
         {
            IFCData paramVal = IFCDataUtil.CreateAsInteger(numberOfDaylightSensors);
            PropertyDescription propertyDescription = new PropertyDescription("NumberOfDaylightSensors");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }

         double designIlluminance = 0.0;
         if (ParameterUtil.GetDoubleValueFromElement(element, "Design Illuminance", out designIlluminance) != null)
         {
            double scaledValue = UnitUtil.ScaleIlluminance(designIlluminance);
            IFCData paramVal = IFCDataUtil.CreateAsIlluminanceMeasure(designIlluminance);
            PropertyDescription propertyDescription = new PropertyDescription("DesignIlluminance");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }

         if (GetStringValueFromElement(element, "Lighting Controls Type", out paramValue) != null)
         {
            IFCData paramVal = IFCDataUtil.CreateAsLabel(paramValue);
            PropertyDescription propertyDescription = new PropertyDescription("LightingControlsType");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }

         if (properties.Count > 0)
         {
            string psetGuid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(element, "ePset_SpatialZoneEnergyAnalysis"));
            return IFCInstanceExporter.CreatePropertySet(file, psetGuid, 
               ExporterCacheManager.OwnerHistoryHandle, "ePset_SpatialZoneEnergyAnalysis", null, properties);
         }

         return null;
      }

      /// <summary>
      /// Creates zone common property set.
      /// </summary>
      /// <param name="file">The file.</param>
      /// <param name="element">The element.</param>
      /// <param name="index">The index to use for the shared parameter names.</param>
      /// <returns>The handle.</returns>
      /// <summary>
      /// Creates the ePset_SpaceOccupant.
      /// </summary>
      /// <param name="exporterIFC">The exporter.</param>
      /// <param name="file">The file.</param>
      /// <param name="element">The element.</param>
      /// <returns>The handle.</returns>
      private static IFCAnyHandle CreatePSetSpaceOccupant(IFCFile file, Element element)
      {
         HashSet<IFCAnyHandle> properties = new HashSet<IFCAnyHandle>();

         string paramValue = "";
         if (GetStringValueFromElement(element, "Space Occupant Organization Abbreviation", out paramValue) != null)
         {
            IFCData paramVal = IFCDataUtil.CreateAsLabel(paramValue);
            PropertyDescription propertyDescription = new PropertyDescription("SpaceOccupantOrganizationAbbreviation");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file,
               propertyDescription, paramVal, null);
            properties.Add(propSingleValue);
         }

         if (ParameterUtil.GetStringValueFromElement(element, "Space Occupant Organization Name", out paramValue) != null)
         {
            IFCData paramVal = IFCDataUtil.CreateAsLabel(paramValue);
            PropertyDescription propertyDescription = new PropertyDescription("SpaceOccupantOrganizationName");
            IFCAnyHandle propSingleValue = IFCInstanceExporter.CreatePropertySingleValue(file, propertyDescription,
               paramVal, null);
            properties.Add(propSingleValue);
         }

         if (properties.Count > 0)
         {
            string psetGuid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(element, "ePset_SpaceOccupant"));
            return IFCInstanceExporter.CreatePropertySet(file, psetGuid, 
               ExporterCacheManager.OwnerHistoryHandle, "ePset_SpaceOccupant", null, properties);
         }

         return null;
      }

      static void CreateOneCOBIEClassificationReference(IFCFile file, Element element, 
         IFCAnyHandle spaceHnd, IDictionary<string, IFCAnyHandle> classificationHandles,
         string name, string location)
      {
         IFCAnyHandle handle = null;
         if (classificationHandles?.TryGetValue(name, out handle) ?? false)
         {
            if (!IFCAnyHandleUtil.IsNullOrHasNoValue(handle))
               return;
         }

         string itemReference;
         if (ParameterUtil.GetStringValueFromElementOrSymbol(element, name + " Reference", out itemReference) != null)
         {
            string itemName;
            ParameterUtil.GetStringValueFromElementOrSymbol(element, name + " Name", out itemName);

            ClassificationReferenceKey key = new ClassificationReferenceKey(location, 
               itemReference, itemName, null, null);
            string guid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(element, "IfcRelAssociatesClassification: " + name));
            IFCAnyHandle classificationReference = ExporterCacheManager.ClassificationCache.AddRelation(
               file, key, guid, null, spaceHnd);

            if (classificationHandles != null)
               classificationHandles[name] = classificationReference;
         }
      }

      /// <summary>
      /// Collect information to create space occupants and cache them to create when end export.
      /// </summary>
      /// <param name="exporterIFC">The exporterIFC object.</param>
      /// <param name="file">The IFCFile object.</param>
      /// <param name="element">The element.</param>
      /// <param name="spaceHnd">The space handle.</param>
      public static void CreateSpaceOccupantInfo(IFCFile file, Element element, IFCAnyHandle spaceHnd)
      {
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(spaceHnd))
         {
            return;
         }

         bool exportToCOBIE = ExporterCacheManager.ExportOptionsCache.ExportAsCOBIE;

         string name;
         if (ParameterUtil.GetStringValueFromElement(element, "Occupant", out name) != null)
         {
            string location = exportToCOBIE ?
               ExporterCacheManager.ClassificationCache.GetBIMStandardsURL(element) : null;

            Dictionary<string, IFCAnyHandle> classificationReferences;
            SpaceOccupantInfo spaceOccupantInfo = ExporterCacheManager.SpaceOccupantInfoCache.Find(name);
            if (spaceOccupantInfo == null)
            {
               classificationReferences = new Dictionary<string, IFCAnyHandle>();
               spaceOccupantInfo = new SpaceOccupantInfo(spaceHnd, classificationReferences, null);
               ExporterCacheManager.SpaceOccupantInfoCache.Register(name, spaceOccupantInfo);
            }
            else
            {
               spaceOccupantInfo.RoomHandles.Add(spaceHnd);
               classificationReferences = spaceOccupantInfo.ClassificationReferences;
            }

            if (exportToCOBIE)
            {
               CreateOneCOBIEClassificationReference(file, element, spaceHnd, classificationReferences,
                  "Space Occupant Organization ID", location);

               CreateOneCOBIEClassificationReference(file, element, spaceHnd, classificationReferences,
                  "Space Occupant Sub-Organization ID", location);

               CreateOneCOBIEClassificationReference(file, element, spaceHnd, classificationReferences,
                  "Space Occupant Organization Billing ID", location);
            }

            if (IFCAnyHandleUtil.IsNullOrHasNoValue(spaceOccupantInfo.SpaceOccupantProperySetHandle))
            {
               // Look for Parameter Set definition.  We don't use the general approach as
               // Space Occupants are not "real" elements.
               spaceOccupantInfo.SpaceOccupantProperySetHandle = 
                  CreatePSetSpaceOccupant(file, element);
            }
         }
      }

      static private void CreateOneGSAClassification(IFCFile file, ZoneInfo zoneInfo, 
         string name, string location, Element element)
      {
         if (!zoneInfo.ClassificationReferences.ContainsKey(name))
         {
            if (ParameterUtil.GetStringValueFromElementOrSymbol(element, name + " Reference", out string itemReference) != null)
            {
               ParameterUtil.GetStringValueFromElementOrSymbol(element, name + " Name", out string itemName);

               ClassificationReferenceKey key = new ClassificationReferenceKey(location,
                  itemReference, itemName, null, null);
               IFCAnyHandle classificationReference =
                  ExporterCacheManager.ClassificationCache.FindOrCreateClassificationReference(file, key);
               zoneInfo.ClassificationReferences[name] = classificationReference;
            }
         }
      }

      static private void CreateGSAInformation(IFCFile file, Element element, 
         ZoneInfo zoneInfo, string zoneObjectType)
      {
         bool isSpatialZone = NamingUtil.IsEqualIgnoringCaseAndSpaces(zoneObjectType, "SpatialZone");
         if (isSpatialZone)
         {
            // Classifications.
            string location = ExporterCacheManager.ClassificationCache.GetBIMStandardsURL(element);

            // Spatial Zone Type (Owner)
            CreateOneGSAClassification(file, zoneInfo, "Spatial Zone Type (Owner)", location, element);

            // Spatial Zone Security Level (Owner)
            CreateOneGSAClassification(file, zoneInfo, "Spatial Zone Security Level (Owner)", location, element);

            // Spatial Zone Type (Energy Analysis)
            if (!zoneInfo.ClassificationReferences.ContainsKey("ASHRAE Zone Type"))
            { 
               if (ParameterUtil.GetStringValueFromElementOrSymbol(element, "ASHRAE Zone Type", 
                  out string itemName) != null)
               {
                  ClassificationReferenceKey key = new ClassificationReferenceKey("ASHRAE 90.1", 
                     "Common Space Type", itemName, null, null);
                  IFCAnyHandle classificationReference =
                     ExporterCacheManager.ClassificationCache.FindOrCreateClassificationReference(file, key);
                  zoneInfo.ClassificationReferences["ASHRAE Zone Type"] = classificationReference;
               }
            }
         }

         if (isSpatialZone || NamingUtil.IsEqualIgnoringCaseAndSpaces(zoneObjectType, "EnergyAnalysisZone"))
         {
            // Property Sets.  We don't use the generic Property Set mechanism because Zones aren't "real" elements.
            CreateSpatialZoneEnergyAnalysisPSet(file, element);
         }
      }

      /// <summary>
      /// Collect information to create zones and cache them to create when end export.
      /// </summary>
      /// <param name="file">The IFCFile object.</param>
      /// <param name="element">The element.</param>
      /// <param name="spaceHandle">The space handle.</param>
      static public void CreateZoneInfos(IFCFile file, Element element, IFCAnyHandle spaceHandle)
      {
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(spaceHandle))
         {
            return;
         }

         bool exportToCOBIE = ExporterCacheManager.ExportOptionsCache.ExportAsCOBIE;

         // Extra zone information, since Revit doesn't have architectural zones.
         ZoneInfoFinder zoneInfoFinder = new ZoneInfoFinder();

         do
         {
            bool hasZoneValues = zoneInfoFinder.SetPropZoneValues(element);
            if (!hasZoneValues)
               break;

            // If we have an empty zone name, but the value exists, keep looking to make sure there aren't valid values later.
            string zoneName = zoneInfoFinder.GetPropZoneValue(ZoneInfoLabel.Name);
            if (string.IsNullOrWhiteSpace(zoneName))
               continue;

            string zoneClassificationCode = zoneInfoFinder.GetPropZoneValue(ZoneInfoLabel.ClassificationCode);
            
            ZoneInfo zoneInfo = ExporterCacheManager.ZoneInfoCache.Find(zoneName);
            if (zoneInfo == null)
            {
               zoneInfo = new ZoneInfo(zoneInfoFinder, spaceHandle);
               zoneInfo.CollectZoneCommonPSetData(file, element, zoneInfoFinder.CurrentZoneNumber);
               zoneInfo.ConditionalAddClassification(file, zoneClassificationCode);
               ExporterCacheManager.ZoneInfoCache.Register(zoneName, zoneInfo);
            }
            else
            {
               // If description, long name or object type were empty, overwrite.
               zoneInfo.UpdateZoneInfo(zoneInfoFinder);
               zoneInfo.RoomHandles.Add(spaceHandle);
               zoneInfo.CollectZoneCommonPSetData(file, element, zoneInfoFinder.CurrentZoneNumber);
               zoneInfo.ConditionalAddClassification(file, zoneClassificationCode);
            }

            if (exportToCOBIE)
            {
               string zoneObjectType = zoneInfoFinder.GetPropZoneValue(ZoneInfoLabel.ObjectType);
               CreateGSAInformation(file, element, zoneInfo, zoneObjectType);
            }
         } while (zoneInfoFinder.IncrementCount()); // prevent infinite loop.
      }

      /// <summary>
      /// Determine if the export instance type is compatible with IfcZone.
      /// </summary>
      /// <param name="exportType">The export type to check.</param>
      /// <returns>True if non-null, and an entity instance type that can go in a zone.</returns>
      public static bool IsZoneCompatible(IFCExportInfoPair exportType)
      {
         if (exportType == null)
            return false;

         return exportType.ExportInstance == IFCEntityType.IfcSpace ||
            exportType.ExportInstance == IFCEntityType.IfcSpatialZone;
      }
   }
}