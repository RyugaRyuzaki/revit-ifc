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
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.DB.IFC;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Steel;
using Autodesk.Revit.DB.Structure;
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Extensions;
using Revit.IFC.Common.Utility;
using Revit.IFC.Export.Exporter.PropertySet;
using Revit.IFC.Export.Properties;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Export.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// This class implements the methods of interface IExternalDBApplication to register the IFC export client to Autodesk Revit.
   /// </summary>
   class ExporterApplication : IExternalDBApplication
   {
      #region IExternalDBApplication Members

      /// <summary>
      /// The method called when Autodesk Revit exits.
      /// </summary>
      /// <param name="application">Controlled application to be shutdown.</param>
      /// <returns>Return the status of the external application.</returns>
      public ExternalDBApplicationResult OnShutdown(Autodesk.Revit.ApplicationServices.ControlledApplication application)
      {
         return ExternalDBApplicationResult.Succeeded;
      }

      /// <summary>
      /// The method called when Autodesk Revit starts.
      /// </summary>
      /// <param name="application">Controlled application to be loaded to Autodesk Revit process.</param>
      /// <returns>Return the status of the external application.</returns>
      public ExternalDBApplicationResult OnStartup(Autodesk.Revit.ApplicationServices.ControlledApplication application)
      {
         // As an ExternalServer, the exporter cannot be registered until full application initialization. Setup an event callback to do this
         // at the appropriate time.
         application.ApplicationInitialized += OnApplicationInitialized;
         return ExternalDBApplicationResult.Succeeded;
      }

      #endregion

      /// <summary>
      /// The action taken on application initialization.
      /// </summary>
      /// <param name="sender">The sender.</param>
      /// <param name="eventArgs">The event args.</param>
      private void OnApplicationInitialized(object sender, EventArgs eventArgs)
      {
         SingleServerService service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.IFCExporterService) as SingleServerService;
         if (service != null)
         {
            Exporter exporter = new Exporter();
            service.AddServer(exporter);
            service.SetActiveServer(exporter.GetServerId());
         }
         // TODO log this failure accordingly
      }
   }

   /// <summary>
   /// This class implements the method of interface IExporterIFC to perform an export to IFC.
   /// </summary>
   public class Exporter : IExporterIFC
   {
      RevitStatusBar statusBar = null;

      // Used for debugging tool "WriteIFCExportedElements"
      private StreamWriter m_Writer;

      private IFCFile m_IfcFile;

      // Allow a derived class to add Element exporter routines.
      public delegate void ElementExporter(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document);

      protected ElementExporter m_ElementExporter = null;

      // Allow a derived class to add property sets.
      public delegate void PropertySetsToExport(IList<IList<PropertySetDescription>> propertySets);

      // Allow a derived class to add predefined property sets.
      public delegate void PreDefinedPropertySetsToExport(IList<IList<PreDefinedPropertySetDescription>> propertySets);

      // Allow a derived class to add quantities.
      public delegate void QuantitiesToExport(IList<IList<QuantityDescription>> propertySets);

      protected QuantitiesToExport m_QuantitiesToExport = null;

      #region IExporterIFC Members

      /// <summary>
      /// Create the list of element export routines.  Each routine will export a subset of Revit elements,
      /// allowing for a choice of which elements are exported, and in what order.
      /// This routine is protected, so it could be overriden by an Exporter class that inherits from this base class.
      /// </summary>
      protected virtual void InitializeElementExporters()
      {
         // Allow another function to potentially add exporters before ExportSpatialElements.
         if (m_ElementExporter == null)
            m_ElementExporter = ExportSpatialElements;
         else
            m_ElementExporter += ExportSpatialElements;

         // Before we export non-spatial elements, we have to temporarily create all parts if we are
         // going to use them.  This is because parts are affected by other elements.
         m_ElementExporter += ExportParts;

         m_ElementExporter += ExportNonSpatialElements;
         m_ElementExporter += ExportContainers;
         m_ElementExporter += ExportGrids;
         m_ElementExporter += ExportConnectors;
         // export AdvanceSteel elements
         m_ElementExporter += ExportAdvanceSteelElements;
      }

      private void ExportHostDocument(ExporterIFC exporterIFC, Document document, View filterView)
      {
         BeginExport(exporterIFC, document, filterView);
         BeginHostDocumentExport(exporterIFC, document);

         m_ElementExporter?.Invoke(exporterIFC, document);

         EndHostDocumentExport(exporterIFC, document);
      }

      private void ExportLinkedDocument(ExporterIFC exporterIFC, ElementId linkId, Document document,
         string guid, Transform linkTrf)
      {
         using (IFCLinkDocumentExportScope linkScope = new IFCLinkDocumentExportScope(document))
         {
            ExporterStateManager.CurrentLinkId = linkId;
            exporterIFC.SetCurrentExportedDocument(document);

            BeginLinkedDocumentExport(exporterIFC, document, guid);
            m_ElementExporter?.Invoke(exporterIFC, document);
            EndLinkedDocumentExport(exporterIFC, document, linkTrf);
         }
      }

      /// <summary>
      /// Implements the method that Autodesk Revit will invoke to perform an export to IFC.
      /// </summary>
      /// <param name="document">The document to export.</param>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="filterView">The view whose filter visibility settings govern the export.</param>
      /// <remarks>Note that filterView doesn't control the exported geometry; it only controls which elements
      /// are visible or not. That allows us to, e.g., choose a plan view but get 3D geometry.</remarks>
      public void ExportIFC(Document document, ExporterIFC exporterIFC, View filterView)
      {
         // Make sure our static caches are clear at the start, and end, of export.
         ExporterCacheManager.Clear(true);
         ExporterStateManager.Clear();

         try
         {
            ExporterCacheManager.ExporterIFC = exporterIFC;

            IFCAnyHandleUtil.IFCStringTooLongWarn += (_1) => { document.Application.WriteJournalComment(_1, true); };
            IFCDataUtil.IFCStringTooLongWarn += (_1) => { document.Application.WriteJournalComment(_1, true); };

            ParamExprListener.ResetParamExprInternalDicts();
            InitializeElementExporters();

            ExportHostDocument(exporterIFC, document, filterView);

            IDictionary<long, string> linkInfos =
               ExporterCacheManager.ExportOptionsCache.FederatedLinkInfo;
            if (linkInfos != null)
            {
               foreach (KeyValuePair<long, string> linkInfo in linkInfos)
               {
                  // TODO: Filter out link types that we don't support, and warn, like we
                  // do when exporting separate links.
                  ElementId linkId = new ElementId(linkInfo.Key);
                  RevitLinkInstance rvtLinkInstance = document.GetElement(linkId) as RevitLinkInstance;
                  if (rvtLinkInstance == null)
                     continue;

                  // Don't export the link if it is hidden in the view.  The standard 
                  // filter to determine if we can export an element will return false for
                  // links.
                  GeometryElement geomElem = null;
                  if (filterView != null)
                  {
                     Options options = new Options() { View = filterView };
                     geomElem = rvtLinkInstance.get_Geometry(options);
                     if (geomElem == null)
                        continue;
                  }

                  Document linkedDocument = rvtLinkInstance.GetLinkDocument();
                  if (linkedDocument != null)
                  {
                     Transform linkTrf = rvtLinkInstance.GetTransform();
                     ExporterCacheManager.Clear(false);
                     ExportLinkedDocument(exporterIFC, rvtLinkInstance.Id, linkedDocument, 
                        linkInfo.Value, linkTrf);
                  }
               }
            }

            IFCFileDocumentInfo ifcFileDocumentInfo = new IFCFileDocumentInfo(document);
            WriteIFCFile(m_IfcFile, ifcFileDocumentInfo);
         }
         catch
         {
            // As of Revit 2022.1, there are no EDM errors to report, because we use ODA.
            FailureMessage fm = new FailureMessage(BuiltInFailures.ExportFailures.IFCFatalExportError);
            document.PostFailure(fm);
         }
         finally
         {
            ExporterCacheManager.Clear(true);
            ExporterStateManager.Clear();

            DelegateClear();
            IFCAnyHandleUtil.EventClear();
            IFCDataUtil.EventClear();

            m_Writer?.Close();

            m_IfcFile?.Close();
            m_IfcFile = null;
         }
      }

      public virtual string GetDescription()
      {
         return "IFC open source exporter";
      }

      public virtual string GetName()
      {
         return "IFC exporter";
      }

      public virtual Guid GetServerId()
      {
         return new Guid("BBE27F6B-E887-4F68-9152-1E664DAD29C3");
      }

      public virtual string GetVendorId()
      {
         return "IFCX";
      }

      // This is not virtual, and should not be overriden.
      public Autodesk.Revit.DB.ExternalService.ExternalServiceId GetServiceId()
      {
         return Autodesk.Revit.DB.ExternalService.ExternalServices.BuiltInExternalServices.IFCExporterService;
      }

      #endregion

      /// <summary>
      /// Exports the AdvanceSteel specific elements
      /// </summary>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      /// <param name="document">The Revit document.</param>
      protected void ExportAdvanceSteelElements(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         // verify if Steel elements should be exported
         if (ExporterCacheManager.ExportOptionsCache.IncludeSteelElements)
         {
            try
            {
               //Firstly, looking for SteelConnections assembly in addin folder.
               string dllPath = Assembly.GetExecutingAssembly().Location;
               Assembly assembly;
               if (File.Exists(Path.GetDirectoryName(dllPath) + @"\Autodesk.SteelConnections.ASIFC.dll"))
                  assembly = Assembly.LoadFrom(Path.GetDirectoryName(dllPath) + @"\Autodesk.SteelConnections.ASIFC.dll");
               else
                  assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, @"Addins\SteelConnections\Autodesk.SteelConnections.ASIFC.dll"));

               if (assembly != null)
               {
                  Type type = assembly.GetType("Autodesk.SteelConnections.ASIFC.ASExporter");
                  if (type != null)
                  {
                     MethodInfo method = type.GetMethod("ExportASElements");
                     if (method != null)
                        method.Invoke(null, new object[] { exporterIFC, document });
                  }
               }
            }
            catch
            { }
         }
      }


      /// <summary>
      /// Checks if a spatial element is contained inside a section box, if the box exists.
      /// </summary>
      /// <param name="sectionBox">The section box.</param>
      /// <param name="element">The element.</param>
      /// <returns>False if there is a section box and the element can be determined to not be inside it.</returns>
      private bool SpatialElementInSectionBox(BoundingBoxXYZ sectionBox, Element element)
      {
         if (sectionBox == null)
            return true;

         BoundingBoxXYZ elementBBox = element.get_BoundingBox(null);
         if (elementBBox == null)
         {
            // Areas don't have bounding box geometry.  For these, try their location point.
            LocationPoint locationPoint = element.Location as LocationPoint;
            if (locationPoint == null)
               return false;

            elementBBox = new BoundingBoxXYZ();
            elementBBox.set_Bounds(0, locationPoint.Point);
            elementBBox.set_Bounds(1, locationPoint.Point);
         }

         return GeometryUtil.BoundingBoxesOverlap(elementBBox, sectionBox);
      }

      private bool ShouldCreateTemporaryParts(Element element, IFCExportInfoPair exportType)
      {

         if (exportType.ExportInstance == IFCEntityType.IfcCovering ||
            exportType.ExportInstance == IFCEntityType.IfcRoof ||
            exportType.ExportInstance == IFCEntityType.IfcSlab ||
            exportType.ExportInstance == IFCEntityType.IfcWall ||
            exportType.ExportInstance == IFCEntityType.IfcWallStandardCase)
         {
            return true;
         }

         ElementId categoryId = CategoryUtil.GetSafeCategoryId(element);
         if (categoryId == new ElementId(BuiltInCategory.OST_Ceilings) ||
            categoryId == new ElementId(BuiltInCategory.OST_Roofs) ||
            categoryId == new ElementId(BuiltInCategory.OST_Floors) ||
            categoryId == new ElementId(BuiltInCategory.OST_Walls))
         {
            return true;
         }

         return false;
      }

      private bool NeedBuilding()
      {
         if (!IFCAnyHandleUtil.IsNullOrHasNoValue(ExporterCacheManager.BuildingHandle))
         {
            return false;
         }

         if (!ExporterCacheManager.SiteExportInfo.IsSiteExported())
         {
            return true;
         }

         return ExporterCacheManager.ExportOptionsCache.ExportLinkedFileAs == LinkedFileExportAs.ExportSameSite;
      }

      private void ExportSite(ExporterIFC exporterIFC, Document document)
      {
         // First create IfcSite with TopographySurface if any.
         // Second examine all Elements in document that would be exported.  If any of them would be exported to IfcSite, do that.
         // Finally, export default IfcSite if the above doesn't apply.
         // 
         // Site and Building need to be created first to ensure containment override to work
         ICollection<ElementId> nonSpatialElementIds = ElementFilteringUtil.GetNonSpatialElements(document, exporterIFC);

         foreach (ElementId topoElemId in nonSpatialElementIds)
         {
            // Note that the TopographySurface exporter in ExportElementImpl does nothing if the
            // element has already been processed here.
            Element topoElem = document.GetElement(topoElemId);
            if (!(topoElem is TopographySurface))
            {
               continue;
            }

            if (ExportElement(exporterIFC, topoElem))
            {
               ExporterCacheManager.SiteExportInfo.SiteElementId = topoElemId;
               break; // Process only the first exportable one to create the IfcSite
            }
         }

         // Next check if there are any non-spatial Elements that are "Exporting As" IfcSite.  If one is found, Export default
         // IfcSite on behalf of that Element.
         if (!ExporterCacheManager.SiteExportInfo.IsSiteExported())
         {
            foreach (ElementId elementId in nonSpatialElementIds)
            {
               Element element = document.GetElement(elementId);
               if (element == null)
                  continue;

               if (SiteExporter.ShouldExportElementAsSite(element))
               {
                  ExporterCacheManager.SiteExportInfo.PotentialSiteElementId = elementId;
                  if (ExportElement(exporterIFC, element))
                  {
                     ExporterCacheManager.SiteExportInfo.EstablishPotentialSiteElement();
                     break;
                  }
               }
            }
         }

         // Finally, just export a Default IfcSite if no other Elements export as IfcSite.
         if (!ExporterCacheManager.SiteExportInfo.IsSiteExported())
         {
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               SiteExporter.ExportDefaultSite(exporterIFC, document, productWrapper);
               ExporterUtil.ExportRelatedProperties(exporterIFC, document.ProjectInformation, productWrapper);
            }
         }
      }

      protected void ExportSpatialElements(ExporterIFC exporterIFC, Document document)
      {
         ExportSite(exporterIFC, document);

         // Create IfcBuilding first here
         if (NeedBuilding())
         {
            IFCAnyHandle facilityPlacement = CreateBuildingPlacement(exporterIFC.GetFile());
            CreateFacilityFromProjectInfo(exporterIFC, document, facilityPlacement, true);
         }

         ExportOptionsCache exportOptionsCache = ExporterCacheManager.ExportOptionsCache;
         View filterView = exportOptionsCache.FilterViewForExport;

         bool exportIfBoundingBoxIsWithinViewExtent = (exportOptionsCache.ExportRoomsInView && filterView is View3D);
         // We don't want to use the filter view for exporting spaces if exportOptionsCache.ExportRoomsInView
         // is true and we have a 3D view.
         bool useFilterViewInCollector = !exportIfBoundingBoxIsWithinViewExtent;

         ISet<ElementId> exportedSpaces = null;
         if (exportOptionsCache.SpaceBoundaryLevel == 2)
            exportedSpaces = SpatialElementExporter.ExportSpatialElement2ndLevel(this, exporterIFC, document);

         // Export all spatial elements for no or 1st level room boundaries; for 2nd level, export spaces that 
         // couldn't be exported above.
         // Note that FilteredElementCollector is one use only, so we need to create a new one here.
         FilteredElementCollector spatialElementCollector = ElementFilteringUtil.GetExportElementCollector(document, useFilterViewInCollector);
         SpatialElementExporter.InitializeSpatialElementGeometryCalculator(document);
         ElementFilter spatialElementFilter = ElementFilteringUtil.GetSpatialElementFilter(document, exporterIFC);
         spatialElementCollector.WherePasses(spatialElementFilter);

         // if the view is 3D and section box is active, then set the section box
         BoundingBoxXYZ sectionBox = null;
         if (exportIfBoundingBoxIsWithinViewExtent)
         {
            View3D currentView = filterView as View3D;
            sectionBox = currentView != null && currentView.IsSectionBoxActive ? currentView.GetSectionBox() : null;
         }

         int numOfSpatialElements = spatialElementCollector.Count<Element>();
         int spatialElementCount = 1;

         foreach (Element element in spatialElementCollector)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingSpatialElements, spatialElementCount, numOfSpatialElements, element.Id));
            spatialElementCount++;

            if ((element == null) || (exportedSpaces != null && exportedSpaces.Contains(element.Id)))
               continue;
            if (ElementFilteringUtil.IsRoomInInvalidPhase(element))
               continue;
            // If the element's bounding box doesn't intersect the section box then ignore it.
            // If the section box isn't active, then we export the element.
            if (!SpatialElementInSectionBox(sectionBox, element))
               continue;
            ExportElement(exporterIFC, element);
         }

         SpatialElementExporter.DestroySpatialElementGeometryCalculator();
      }

      protected void ExportParts(ExporterIFC exporterIFC, Document document)
      {
         // This is only for IFC4 Reference View.
         if (!ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
            return;

         // This is non-optimized for now.
         ICollection<ElementId> nonSpatialElementIds = ElementFilteringUtil.GetNonSpatialElements(document, exporterIFC);
         List<ElementId> hostIds = new();

         foreach (ElementId nonSpatialElementId in nonSpatialElementIds)
         {
            // We only support a few entities, check here.
            Element element = document.GetElement(nonSpatialElementId);
            IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(element, out _);
            if (!ShouldCreateTemporaryParts(element, exportType))
            {
               continue;
            }

            // Already has parts, no need to create.
            ICollection<ElementId> associatedParts = PartUtils.GetAssociatedParts(document, nonSpatialElementId, false, true);
            if (associatedParts.Count > 0)
            {
               continue;
            }

            List<ElementId> ids = new() { nonSpatialElementId };
            if (!PartUtils.AreElementsValidForCreateParts(document, ids))
            {
               continue;
            }

            hostIds.Add(nonSpatialElementId);
         }

         int numPartIds = hostIds.Count;
         if (numPartIds == 0)
         {
            return;
         }

         using (SubTransaction st = new SubTransaction(document))
         {
            st.Start();

            PartUtils.CreateParts(document, hostIds);
            document.Regenerate();

            foreach (ElementId hostId in hostIds)
            {
               List<GeometryElement> partGeometries = [];

               ICollection<ElementId> associatedPartIds = PartUtils.GetAssociatedParts(document, hostId, false, true);
               foreach (ElementId associatedPartId in associatedPartIds)
               {
                  Element part = document.GetElement(associatedPartId);
                  if (part == null)
                     continue;

                  View ownerView = ExporterUtil.GetViewForElementGeometry(part);
                  Options options = (ownerView == null) ?
                     GeometryUtil.GetIFCExportGeometryOptions() :
                     new() { View = ownerView };

                  GeometryElement geomElem = part.get_Geometry(options);
                  if (geomElem == null)
                     continue;

                  GeometryElement geomElemClone = geomElem.GetTransformed(Transform.Identity);

                  ExporterCacheManager.TemporaryPartsCache.CollectTemporaryPartInfo(exporterIFC, geomElemClone, part);

                  if (ExporterCacheManager.TemporaryPartsCache.TemporaryPartPresentationLayer == null)
                     ExporterCacheManager.TemporaryPartsCache.TemporaryPartPresentationLayer = 
                        exporterIFC.GetLayerNameForPresentationLayer(part, CategoryUtil.GetSafeCategoryId(part)) ?? "";

                  partGeometries.Add(geomElemClone);

               }
               ExporterCacheManager.TemporaryPartsCache.Register(hostId, partGeometries);
            }

            st.RollBack();
         }
      }

      protected void ExportNonSpatialElements(ExporterIFC exporterIFC, Document document)
      {
         // Cache non-spatial Elements for later usage.
         ICollection<ElementId> nonSpatialElements = ElementFilteringUtil.GetNonSpatialElements(document, exporterIFC);

         int numNonSpatialElements = nonSpatialElements.Count;
         int otherElementCollectorCount = 1;
         foreach (ElementId elementId in nonSpatialElements)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingNonSpatialElements, otherElementCollectorCount, numNonSpatialElements, elementId));

            if (ExporterCacheManager.SiteExportInfo.IsSiteElementId(elementId))
               continue;

            otherElementCollectorCount++;
            Element element = document.GetElement(elementId);
            if (element != null)
            {
               ExportElement(exporterIFC, element);
            }
         }
      }

      /// <summary>
      /// Export various containers that depend on individual element export.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportContainers(ExporterIFC exporterIFC, Document document)
      {
         using (ExporterStateManager.ForceElementExport forceElementExport = new ExporterStateManager.ForceElementExport())
         {
            ExportCachedRailings(exporterIFC, document);
            ExportCachedFabricAreas(exporterIFC, document);
            ExportTrusses(exporterIFC, document);
            ExportBeamSystems(exporterIFC, document);
            ExportAreaSchemes(exporterIFC, document);
            ExportGroups(exporterIFC, document);
            ExportZones(exporterIFC, document);
         }
      }

      /// <summary>
      /// Export railings cached during spatial element export.  
      /// Railings are exported last as their containment is not known until all stairs have been exported.
      /// This is a very simple sorting, and further containment issues could require a more robust solution in the future.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportCachedRailings(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         HashSet<ElementId> railingCollection = ExporterCacheManager.RailingCache;
         int railingIndex = 1;
         int railingCollectionCount = railingCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.RailingCache)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingRailings, railingIndex, railingCollectionCount, elementId));
            railingIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Export FabricAreas cached during non-spatial element export.  
      /// We export whatever FabricAreas actually have handles as IfcGroup.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportCachedFabricAreas(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         IDictionary<ElementId, HashSet<IFCAnyHandle>> fabricAreaCollection = ExporterCacheManager.FabricAreaHandleCache;
         int fabricAreaIndex = 1;
         int fabricAreaCollectionCount = fabricAreaCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.FabricAreaHandleCache.Keys)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingFabricAreas, fabricAreaIndex, fabricAreaCollectionCount, elementId));
            fabricAreaIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Export Trusses.  These could be in assemblies, so do before assembly export, but after beams and members are exported.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportTrusses(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         HashSet<ElementId> trussCollection = ExporterCacheManager.TrussCache;
         int trussIndex = 1;
         int trussCollectionCount = trussCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.TrussCache)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingTrusses, trussIndex, trussCollectionCount, elementId));
            trussIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Export BeamSystems.  These could be in assemblies, so do before assembly export, but after beams are exported.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportBeamSystems(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         HashSet<ElementId> beamSystemCollection = ExporterCacheManager.BeamSystemCache;
         int beamSystemIndex = 1;
         int beamSystemCollectionCount = beamSystemCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.BeamSystemCache)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingBeamSystems, beamSystemIndex, beamSystemCollectionCount, elementId));
            beamSystemIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Export Groups.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportGroups(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         HashSet<ElementId> nonEmptyGroups = new(); // all non-empty groups are exported
         Dictionary<ElementId, bool> emptyGroups = new(); // <group id, exportFlag> some empty groups are exported
         
         foreach (ElementId groupId in ExporterCacheManager.GroupCache.Keys)
         {
            if (ExporterCacheManager.GroupCache.IsEmptyGroup(groupId))
               emptyGroups.Add(groupId, false);
            else
               nonEmptyGroups.Add(groupId);
         }

         // Export groups that are super groups of non-empty groups should be exported
         foreach (ElementId groupId in nonEmptyGroups)
         {
            Element groupElement = document.GetElement(groupId);
            ElementId parentGroupId = groupElement?.GroupId ?? ElementId.InvalidElementId;

            while (parentGroupId != ElementId.InvalidElementId)
            {
               if (emptyGroups.ContainsKey(parentGroupId))
                  emptyGroups[parentGroupId] = true; // mark to export

               Element parentGroupElement = document.GetElement(parentGroupId);
               parentGroupId = parentGroupElement.GroupId ?? ElementId.InvalidElementId;
            }
         }

         List<ElementId> groupsToExport = emptyGroups
            .Where(k => k.Value == true)
            .Select(x => x.Key)
            .Union(nonEmptyGroups)
            .ToList();

         // Export non-empty groups
         int groupIndex = 1;
         int groupCollectionCount = groupsToExport.Count;
         foreach (ElementId elementId in groupsToExport)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingGroups, groupIndex, groupCollectionCount, elementId));
            groupIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }

         // Relate group elements to exported group entities
         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

         foreach (KeyValuePair<ElementId, GroupInfo> groupEntry in ExporterCacheManager.GroupCache)
         {
            GroupInfo groupInfo = groupEntry.Value;
            IFCAnyHandle groupHandle = groupInfo?.GroupHandle;
            HashSet<IFCAnyHandle> elementHandles = groupInfo?.ElementHandles;

            if (groupHandle == null ||
               (elementHandles?.Count ?? 0) == 0 ||
               (groupInfo.GroupType?.ExportInstance ?? IFCEntityType.UnKnown) == IFCEntityType.UnKnown)
               continue;

            if (elementHandles.Contains(groupHandle))
            {
               elementHandles.Remove(groupHandle);
               if (elementHandles.Count == 0)
                  continue;
            }

            // Group may be exported as IfcFurniture which contains IfcSystemFurnitureElements, so they need a RelAggregates relationship
            if (groupEntry.Value.GroupType.ExportInstance == IFCEntityType.IfcFurniture)
            {
               string guid = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, groupHandle));
               IFCInstanceExporter.CreateRelAggregates(file, guid, ownerHistory, null, null, groupHandle, elementHandles);
            }
            else
            {
               Element group = document.GetElement(groupEntry.Key);
               string guid = GUIDUtil.CreateSubElementGUID(group, (int)IFCGroupSubElements.RelAssignsToGroup);
               IFCInstanceExporter.CreateRelAssignsToGroup(file, guid, ownerHistory, null, null, elementHandles, null, groupHandle);
            }
         }
      }

      /// <summary>
      /// Export Zones.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportZones(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         HashSet<ElementId> zoneCollection = ExporterCacheManager.ZoneCache;
         int zoneIndex = 1;
         int zoneCollectionCount = zoneCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.ZoneCache)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingExportZones, zoneIndex, zoneCollectionCount, elementId));
            zoneIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Export Area Schemes.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportAreaSchemes(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         foreach (ElementId elementId in ExporterCacheManager.AreaSchemeCache.Keys)
         {
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      protected void ExportGrids(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         // Export the grids
         GridExporter.Export(exporterIFC, document);
      }

      protected void ExportConnectors(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         ConnectorExporter.Export(exporterIFC);
      }

      /// <summary>
      /// Determines if the selected element meets extra criteria for export.
      /// </summary>
      /// <param name="exporterIFC">The exporter class.</param>
      /// <param name="element">The current element to export.</param>
      /// <returns>True if the element should be exported.</returns>
      protected virtual bool CanExportElement(ExporterIFC exporterIFC, Autodesk.Revit.DB.Element element)
      {
         // Skip the export of AdvanceSteel elements, they will be exported by ExportASElements
         if (ExporterCacheManager.ExportOptionsCache.IncludeSteelElements)
         {
            // In case Autodesk.Revit.DB.Steel is missing, continue the export.
            try
            {
               SteelElementProperties cell = SteelElementProperties.GetSteelElementProperties(element);
               if (cell != null)
               {
                  bool hasGraphics = false;
                  PropertyInfo graphicsCell = cell.GetType().GetProperty("HasGraphics", BindingFlags.Instance | BindingFlags.NonPublic);
                  if (graphicsCell != null) // Concrete elements with cell that have HasGraphics set to true, must be handled by Revit exporter.
                     hasGraphics = (bool)graphicsCell.GetValue(cell, null);

                  if (hasGraphics)
                     return false;
               }
            }
            catch
            { }
         }

         return ElementFilteringUtil.CanExportElement(element, false);
      }

      /// <summary>
      /// Performs the export of elements, including spatial and non-spatial elements.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="element">The element to export.</param>
      /// <returns>False if the element can't be exported at all, true otherwise.</returns>
      /// <remarks>A true return value doesn't mean something was exported, but that the
      /// routine did a quick reject on the element, or an exception occurred.</remarks>
      public virtual bool ExportElement(ExporterIFC exporterIFC, Element element)
      {
         if (!CanExportElement(exporterIFC, element))
         {
            if (element is RevitLinkInstance && ExporterUtil.ExportingHostModel())
            {
               bool bStoreIFCGUID = ExporterCacheManager.ExportOptionsCache.GUIDOptions.StoreIFCGUID;
               ExporterCacheManager.ExportOptionsCache.GUIDOptions.StoreIFCGUID = true;
               GUIDUtil.CreateGUID(element);
               ExporterCacheManager.ExportOptionsCache.GUIDOptions.StoreIFCGUID = bStoreIFCGUID;
            }
            return false;
         }

         //WriteIFCExportedElements
         if (m_Writer != null)
         {
            string categoryName = CategoryUtil.GetCategoryName(element);
            m_Writer.WriteLine(string.Format("{0},{1},{2}", element.Id, string.IsNullOrEmpty(categoryName) ? "null" : categoryName, element.GetType().Name));
         }

         try
         {
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               ExportElementImpl(exporterIFC, element, productWrapper);
               ExporterUtil.ExportRelatedProperties(exporterIFC, element, productWrapper);
            }

            // We are going to clear the parameter cache for the element (not the type) after the export.
            // We do not expect to need the parameters for this element again, so we can free up the space.
            if (!(element is ElementType) && !ExporterStateManager.ShouldPreserveElementParameterCache(element))
               ParameterUtil.RemoveElementFromCache(element);
         }
         catch (System.Exception ex)
         {
            HandleUnexpectedException(ex, element);
            return false;
         }

         return true;
      }

      /// <summary>
      /// Handles the unexpected Exception.
      /// </summary>
      /// <param name="ex">The unexpected exception.</param>
      /// <param name="element ">The element got the exception.</param>
      internal void HandleUnexpectedException(Exception exception, Element element)
      {
         Document document = element.Document;
         string errMsg = string.Format("IFC error: Exporting element \"{0}\",{1} - {2}", element.Name, element.Id, exception.ToString());
         element.Document.Application.WriteJournalComment(errMsg, true);

         FailureMessage fm = new FailureMessage(BuiltInFailures.ExportFailures.IFCGenericExportWarning);
         fm.SetFailingElement(element.Id);
         document.PostFailure(fm);
      }

      /// <summary>
      /// Checks if the element is MEP type.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="element">The element to check.</param>
      /// <returns>True for MEP type of elements.</returns>
      private bool IsMEPType(Element element, IFCExportInfoPair exportType)
      {
         return (ElementFilteringUtil.IsMEPType(exportType) || ElementFilteringUtil.ProxyForMEPType(element, exportType));
      }

      /// <summary>
      /// Checks if exporting an element as building elment proxy.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>True for exporting as proxy element.</returns>
      private bool ExportAsProxy(Element element, IFCExportInfoPair exportType)
      {
         // FaceWall should be exported as IfcWall.
         return ((element is FaceWall) || (element is ModelText) || (exportType.ExportInstance == IFCEntityType.IfcBuildingElementProxy) || (exportType.ExportType == IFCEntityType.IfcBuildingElementProxyType));
      }

      /// <summary>
      /// Checks if exporting an element of Stairs category.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>True if element is of category OST_Stairs.</returns>
      private bool IsStairs(Element element)
      {
         return (CategoryUtil.GetSafeCategoryId(element) == new ElementId(BuiltInCategory.OST_Stairs));
      }

      /// <summary>
      /// Checks if the element is one of the types that contain structural rebar.
      /// </summary>
      /// <param name="element"></param>
      /// <returns></returns>
      private bool IsRebarType(Element element)
      {
         return (element is AreaReinforcement || element is PathReinforcement || element is Rebar || element is RebarContainer);
      }

      /// <summary>
      /// Implements the export of element.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="element">The element to export.</param>
      /// <param name="productWrapper">The ProductWrapper object.</param>
      public virtual void ExportElementImpl(ExporterIFC exporterIFC, Element element, ProductWrapper productWrapper)
      {
         View ownerView = ExporterUtil.GetViewForElementGeometry(element);
         Options options = (ownerView == null) ?
            GeometryUtil.GetIFCExportGeometryOptions() :
            new Options() { View = ownerView };
         
         GeometryElement geomElem = element.get_Geometry(options);

         // Default: we don't preserve the element parameter cache after export.
         bool shouldPreserveParameterCache = false;

         try
         {
            exporterIFC.PushExportState(element, geomElem);
            IFCFile file = exporterIFC.GetFile();

            Document doc = element.Document;
            using (SubTransaction st = new SubTransaction(doc))
            {
               st.Start();

               // A very quick check if we happen to be exporting only geometry.
               if (ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly)
               {
                  IFCExportInfoPair exportType = new(IFCEntityType.IfcBuildingElementProxy, "ELEMENT");
                  ProxyElementExporter.Export(exporterIFC, element, geomElem, productWrapper);
               }

               // A long list of supported elements.  Please keep in alphabetical order by the first item in the list..
               // Exception:  before handling the List of Elements, check to see if this should be exported as a Main Site.
               // TopologySUrface should be handled in its own method, since it has extra processing.
               else if (ExporterCacheManager.SiteExportInfo.IsPotentialSiteElementId(element.Id))
               {
                  SiteExporter.ExportGenericElementAsSite(exporterIFC, element, geomElem, productWrapper);
               }
               else if (element is AreaScheme)
               {
                  AreaSchemeExporter.ExportAreaScheme(file, element as AreaScheme, productWrapper);
               }
               else if (element is AssemblyInstance)
               {
                  AssemblyInstance assemblyInstance = element as AssemblyInstance;
                  AssemblyInstanceExporter.ExportAssemblyInstanceElement(exporterIFC, assemblyInstance, productWrapper);
               }
               else if (element is BeamSystem)
               {
                  if (ExporterCacheManager.BeamSystemCache.Contains(element.Id))
                     AssemblyInstanceExporter.ExportBeamSystem(exporterIFC, element as BeamSystem, productWrapper);
                  else
                  {
                     ExporterCacheManager.BeamSystemCache.Add(element.Id);
                     shouldPreserveParameterCache = true;
                  }
               }
               else if (element is Ceiling)
               {
                  Ceiling ceiling = element as Ceiling;
                  CeilingExporter.ExportCeilingElement(exporterIFC, ceiling, ref geomElem, productWrapper);
               }
               else if (element is CeilingAndFloor || element is Floor)
               {
                  // This covers both Floors and Building Pads.
                  CeilingAndFloor hostObject = element as CeilingAndFloor;
                  FloorExporter.ExportCeilingAndFloorElement(exporterIFC, hostObject, ref geomElem, productWrapper);
               }
               else if (element is WallFoundation)
               {
                  WallFoundation footing = element as WallFoundation;
                  FootingExporter.ExportFootingElement(exporterIFC, footing, geomElem, productWrapper);
               }
               else if (element is CurveElement)
               {
                  CurveElement curveElem = element as CurveElement;
                  CurveElementExporter.ExportCurveElement(exporterIFC, curveElem, geomElem, productWrapper);
               }
               else if (element is CurtainSystem)
               {
                  CurtainSystem curtainSystem = element as CurtainSystem;
                  CurtainSystemExporter.ExportCurtainSystem(exporterIFC, curtainSystem, productWrapper);
               }
               else if (CurtainSystemExporter.IsLegacyCurtainElement(element))
               {
                  CurtainSystemExporter.ExportLegacyCurtainElement(exporterIFC, element, productWrapper);
               }
               else if (element is DuctInsulation)
               {
                  DuctInsulation ductInsulation = element as DuctInsulation;
                  DuctInsulationExporter.ExportDuctInsulation(exporterIFC, ductInsulation, geomElem, productWrapper);
               }
               else if (element is DuctLining)
               {
                  DuctLining ductLining = element as DuctLining;
                  DuctLiningExporter.ExportDuctLining(exporterIFC, ductLining, geomElem, productWrapper);
               }
               else if (element is ElectricalSystem)
               {
                  ExporterCacheManager.SystemsCache.AddElectricalSystem(element.Id);
               }
               else if (element is FabricArea)
               {
                  // We are exporting the fabric area as a group only.
                  FabricSheetExporter.ExportFabricArea(exporterIFC, element, productWrapper);
               }
               else if (element is FabricSheet)
               {
                  FabricSheet fabricSheet = element as FabricSheet;
                  FabricSheetExporter.ExportFabricSheet(exporterIFC, fabricSheet, geomElem, productWrapper);
               }
               else if (element is FaceWall)
               {
                  WallExporter.ExportWall(exporterIFC, null, element, null, ref geomElem, productWrapper);
               }
               else if (element is FamilyInstance)
               {
                  FamilyInstance familyInstanceElem = element as FamilyInstance;
                  FamilyInstanceExporter.ExportFamilyInstanceElement(exporterIFC, familyInstanceElem, ref geomElem, productWrapper);
               }
               else if (element is FilledRegion)
               {
                  FilledRegion filledRegion = element as FilledRegion;
                  FilledRegionExporter.Export(exporterIFC, filledRegion, productWrapper);
               }
               else if (element is Grid)
               {
                  ExporterCacheManager.GridCache.Add(element);
               }
               else if (element is Group)
               {
                  if (ExporterCacheManager.GroupCache.GetExportFlag(element.Id))
                     GroupExporter.ExportGroupElement(exporterIFC, element as Group, productWrapper);
                  else
                  {
                     ExporterCacheManager.GroupCache.SetExportFlag(element.Id);
                     shouldPreserveParameterCache = true;
                  }
                  
               }
               else if (element is HostedSweep)
               {
                  HostedSweep hostedSweep = element as HostedSweep;
                  HostedSweepExporter.Export(exporterIFC, hostedSweep, geomElem, productWrapper);
               }
               else if (element is Part)
               {
                  Part part = element as Part;
                  if (ExporterCacheManager.ExportOptionsCache.ExportPartsAsBuildingElements)
                     PartExporter.ExportPartAsBuildingElement(exporterIFC, part, geomElem, productWrapper);
                  else
                     PartExporter.ExportStandalonePart(exporterIFC, part, geomElem, productWrapper);
               }
               else if (element is PipeInsulation)
               {
                  PipeInsulation pipeInsulation = element as PipeInsulation;
                  PipeInsulationExporter.ExportPipeInsulation(exporterIFC, pipeInsulation, geomElem, productWrapper);
               }
               else if (element is PropertyLine)
               {
                  PropertyLine propertyLine = element as PropertyLine;
                  CurveElementExporter.ExportPropertyLineElement(exporterIFC, propertyLine, geomElem, productWrapper);
               }
               else if (element is Railing)
               {
                  if (ExporterCacheManager.RailingCache.Contains(element.Id))
                     RailingExporter.ExportRailingElement(exporterIFC, element as Railing, productWrapper);
                  else
                  {
                     ExporterCacheManager.RailingCache.Add(element.Id);
                     RailingExporter.AddSubElementsToCache(element as Railing);
                     shouldPreserveParameterCache = true;
                  }
               }
               else if (RampExporter.IsRamp(element))
               {
                  RampExporter.Export(exporterIFC, element, geomElem, productWrapper);
               }
               else if (IsRebarType(element))
               {
                  RebarExporter.Export(exporterIFC, element, productWrapper);
               }
               else if (element is RebarCoupler)
               {
                  RebarCoupler couplerElem = element as RebarCoupler;
                  RebarCouplerExporter.ExportCoupler(exporterIFC, couplerElem, productWrapper);
               }
               else if (element is RoofBase)
               {
                  RoofBase roofElement = element as RoofBase;
                  RoofExporter.Export(exporterIFC, roofElement, ref geomElem, productWrapper);
               }
               else if (element is SpatialElement)
               {
                  SpatialElement spatialElem = element as SpatialElement;
                  SpatialElementExporter.ExportSpatialElement(exporterIFC, spatialElem, productWrapper);
               }
               else if (IsStairs(element))
               {
                  StairsExporter.Export(exporterIFC, element, geomElem, productWrapper);
               }
               else if (element is TextNote)
               {
                  TextNote textNote = element as TextNote;
                  TextNoteExporter.Export(exporterIFC, textNote, productWrapper);
               }
               else if (element is TopographySurface)
               {
                  TopographySurface topSurface = element as TopographySurface;
                  SiteExporter.ExportTopographySurface(exporterIFC, topSurface, geomElem, productWrapper);
               }
               else if (element is Truss)
               {
                  if (ExporterCacheManager.TrussCache.Contains(element.Id))
                     AssemblyInstanceExporter.ExportTrussElement(exporterIFC, element as Truss, productWrapper);
                  else
                  {
                     ExporterCacheManager.TrussCache.Add(element.Id);
                     shouldPreserveParameterCache = true;
                  }
               }
               else if (element is Wall)
               {
                  Wall wallElem = element as Wall;
                  WallExporter.Export(exporterIFC, wallElem, ref geomElem, productWrapper);
               }
               else if (element is WallSweep)
               {
                  WallSweep wallSweep = element as WallSweep;
                  WallSweepExporter.Export(exporterIFC, wallSweep, geomElem, productWrapper);
               }
               else if (element is Zone)
               {
                  if (ExporterCacheManager.ZoneCache.Contains(element.Id))
                     ZoneExporter.ExportZone(exporterIFC, element as Zone, productWrapper);
                  else
                  {
                     ExporterCacheManager.ZoneCache.Add(element.Id);
                     shouldPreserveParameterCache = true;
                  }
               }
               else
               {
                  string ifcEnumType;
                  IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(element, out ifcEnumType);

                  // Check the intended IFC entity or type name is in the exclude list specified in the UI
                  IFCEntityType elementClassTypeEnum;
                  if (Enum.TryParse(exportType.ExportInstance.ToString(), out elementClassTypeEnum)
                        || Enum.TryParse(exportType.ExportType.ToString(), out elementClassTypeEnum))
                     if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(elementClassTypeEnum))
                        return;

                  // The intention with the code below is to make this the "generic" element exporter, which would export any Revit element as any IFC instance.
                  // We would then in addition have specialized functions that would convert specific Revit elements to specific IFC instances where extra information
                  // could be gathered from the element.
                  bool exported = false;
                  bool elementIsFabricationPart = element is FabricationPart;
                  if (IsMEPType(element, exportType))
                  {
                     exported = GenericMEPExporter.Export(exporterIFC, element, geomElem, exportType, ifcEnumType, productWrapper);
                  }
                  else if (!elementIsFabricationPart && ExportAsProxy(element, exportType))
                  {
                     // Note that we currently export FaceWalls as proxies, and that FaceWalls are HostObjects, so we need
                     // to have this check before the (element is HostObject check.
                     exported = ProxyElementExporter.Export(exporterIFC, element, geomElem, productWrapper, exportType);
                  }
                  else if (elementIsFabricationPart || (element is HostObject) || (element is DirectShape) || (element is MassLevelData))
                  {
                     exported = GenericElementExporter.ExportElement(exporterIFC, element, geomElem, productWrapper);
                  }

                  if (exported)
                  {
                     // For ducts and pipes, we will add a IfcRelCoversBldgElements during the end of export.
                     if (element is Duct || element is Pipe)
                     {
                        ExporterCacheManager.MEPCache.CoveredElementsCache[element.Id] = element.Category?.Id ?? ElementId.InvalidElementId;
                     }
                     // For cable trays and conduits, we might create systems during the end of export.
                     if (element is CableTray || element is Conduit)
                     {
                        ExporterCacheManager.MEPCache.CableElementsCache.Add(element.Id);
                     }
                  }
               }

               if (element.AssemblyInstanceId != ElementId.InvalidElementId)
                  ExporterCacheManager.AssemblyInstanceCache.RegisterElements(element.AssemblyInstanceId, productWrapper);
               if (element.GroupId != ElementId.InvalidElementId)
                  ExporterCacheManager.GroupCache.RegisterElements(element.GroupId, productWrapper);

               st.RollBack();
            }
         }
         finally
         {
            exporterIFC.PopExportState();
            ExporterStateManager.PreserveElementParameterCache(element, shouldPreserveParameterCache);
         }
      }

      /// <summary>
      /// Sets the schema information for the current export options.  This can be overridden.
      /// </summary>
      protected virtual IFCFileModelOptions CreateIFCFileModelOptions(ExporterIFC exporterIFC)
      {
         IFCFileModelOptions modelOptions = new IFCFileModelOptions();
         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x2)
         {
            modelOptions.SchemaName = "IFC2x2_FINAL";
         }
         else if (ExporterCacheManager.ExportOptionsCache.ExportAs4)
         {
            modelOptions.SchemaName = "IFC4";
         }
         else if (ExporterCacheManager.ExportOptionsCache.ExportAs4x3)
         {
            modelOptions.SchemaName = "IFC4x3";   // Temporary until the ODA Toolkit version used supports the final version
         }
         else
         {
            // We leave IFC2x3 as default until IFC4 is finalized and generally supported across platforms.
            modelOptions.SchemaName = "IFC2x3";
         }
         return modelOptions;
      }

      /// <summary>
      /// Sets the lists of property sets to be exported.  This can be overriden.
      /// </summary>
      protected virtual void InitializePropertySets()
      {
         ExporterInitializer.InitPropertySets();
      }

      /// <summary>
      /// Sets the lists of quantities to be exported.  This can be overriden.
      /// </summary>
      protected virtual void InitializeQuantities(IFCVersion fileVersion)
      {
         ExporterInitializer.InitQuantities(m_QuantitiesToExport, ExporterCacheManager.ExportIFCBaseQuantities());
      }

      /// <summary>
      /// Initializes the common properties at the beginning of the export process.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="document">The current document.</param>
      /// <param name="filterView">The optional filter view.</param>
      private void BeginExport(ExporterIFC exporterIFC, Document document, View filterView)
      {
         statusBar = RevitStatusBar.Create();

         ElementFilteringUtil.InitCategoryVisibilityCache();
         NamingUtil.InitNameIncrNumberCache();

         string writeIFCExportedElementsVar = Environment.GetEnvironmentVariable("WriteIFCExportedElements");
         if (writeIFCExportedElementsVar != null && writeIFCExportedElementsVar.Length > 0)
         {
            m_Writer = new StreamWriter(@"c:\ifc-output-filters.txt");
         }

         // cache options
         ExportOptionsCache exportOptionsCache = ExportOptionsCache.Create(exporterIFC, document, filterView);
         ExporterCacheManager.ExportOptionsCache = exportOptionsCache;

         IFCFileModelOptions modelOptions = CreateIFCFileModelOptions(exporterIFC);

         m_IfcFile = IFCFile.Create(modelOptions);
         exporterIFC.SetFile(m_IfcFile);
      }

      private bool ExportBuilding(IList<Level> allLevels)
      {
         foreach (Level level in allLevels)
         {
            if (LevelUtil.IsBuildingStory(level))
               return true;
         }
         return false;
      }

      private void BeginHostDocumentExport(ExporterIFC exporterIFC, Document document)
      {
         ExporterCacheManager.ExportOptionsCache.UpdateForDocument(exporterIFC, document, null);

         // Set language.
         Application app = document.Application;
         string pathName = document.PathName;
         LanguageType langType = LanguageType.Unknown;
         if (!string.IsNullOrEmpty(pathName))
         {
            try
            {
               BasicFileInfo basicFileInfo = BasicFileInfo.Extract(pathName);
               if (basicFileInfo != null)
                  langType = basicFileInfo.LanguageWhenSaved;
            }
            catch
            {
            }
         }
         if (langType == LanguageType.Unknown)
            langType = app.Language;
         ExporterCacheManager.LanguageType = langType;

         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle applicationHandle = CreateApplicationInformation(file, document);

         CreateGlobalCartesianOrigin(exporterIFC);
         CreateGlobalDirection(exporterIFC);
         CreateGlobalDirection2D(exporterIFC);

         // Initialize common properties before creating any rooted entities.
         InitializePropertySets();
         InitializeQuantities(ExporterCacheManager.ExportOptionsCache.FileVersion);

         CreateProject(exporterIFC, document, applicationHandle);

         BeginDocumentExportCommon(exporterIFC, document);
      }

      /// <summary>
      /// Initializes the common properties at the beginning of the export process.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="document">The document to export.</param>
      private void BeginLinkedDocumentExport(ExporterIFC exporterIFC, Document document, string guid)
      {
         ExporterCacheManager.ExportOptionsCache.UpdateForDocument(exporterIFC, document, guid);

         BeginDocumentExportCommon(exporterIFC, document);
      }

      private IFCAnyHandle CreateFacilityPart(ExporterIFC exporterIFC, Level level, string objectType,
         IFCAnyHandle objectPlacement, IFCElementComposition compositionType, double elevation, string predefinedType)
      {
         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

         if (!ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4x3)
         {
            switch (ExporterCacheManager.ExportOptionsCache.FacilityType)
            {
               case KnownFacilityTypes.Bridge:
                  {
                     return IFCInstanceExporter.CreateBridgePart(exporterIFC, level, ownerHistory, objectType,
                        objectPlacement, compositionType, predefinedType);
                  }
               case KnownFacilityTypes.MarineFacility:
                  {
                     return IFCInstanceExporter.CreateMarinePart(exporterIFC, level, ownerHistory, objectType,
                        objectPlacement, compositionType);
                  }
               case KnownFacilityTypes.Railway:
                  {
                     return IFCInstanceExporter.CreateRailwayPart(exporterIFC, level, ownerHistory, objectType,
                        objectPlacement, compositionType);
                  }
               case KnownFacilityTypes.Road:
                  {
                     return IFCInstanceExporter.CreateRoadPart(exporterIFC, level, ownerHistory, objectType,
                        objectPlacement, compositionType);
                  }
            }
         }

         return IFCInstanceExporter.CreateBuildingStorey(exporterIFC, level,
            ownerHistory, objectType, objectPlacement, compositionType, elevation);
      }

      /// <summary>
      /// Initializes the common properties at the beginning of the export process.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="document">The document to export.</param>
      private void BeginDocumentExportCommon(ExporterIFC exporterIFC, Document document)
      {
         // Force GC to avoid finalizer thread blocking and different export time for equal exports
         ForceGarbageCollection();

         IFCFile file = exporterIFC.GetFile();
         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            // create building
            IFCAnyHandle buildingPlacement = CreateBuildingPlacement(file);

            IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

            // create levels
            // Check if there is any level assigned as a building storey, if no at all, model will be exported without Building and BuildingStorey, all containment will be to Site
            List<Level> allLevels = !ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly ? LevelUtil.FindAllLevels(document) : [];

            bool exportBuilding = ExportBuilding(allLevels);

            // Skip Building if there is no Storey to be exported
            if (CreateFacilityFromProjectInfo(exporterIFC, document, buildingPlacement, exportBuilding) != null)
            {        
               IList<Element> unassignedBaseLevels = new List<Element>();

               double lengthScale = UnitUtil.ScaleLengthForRevitAPI();

               IFCAnyHandle prevBuildingStorey = null;
               IFCAnyHandle prevPlacement = null;
               double prevHeight = 0.0;
               double prevElev = 0.0;

               int numLevels = allLevels?.Count ?? 0;
               
               // When exporting to IFC 2x3, we have a limited capability to export some Revit view-specific
               // elements, specifically Filled Regions and Text.  However, we do not have the
               // capability to choose which views to export.  As such, we will choose (up to) one DBView per
               // exported level.
               // TODO: Let user choose which view(s) to export.  Ensure that the user know that only one view
               // per level is supported.
               IDictionary<ElementId, View> views = 
                  LevelUtil.FindViewsForLevels(document, ViewType.FloorPlan, allLevels);

               for (int ii = 0; ii < numLevels; ii++)
               {
                  Level level = allLevels[ii];
                  if (level == null)
                     continue;

                  IFCLevelInfo levelInfo = null;

                  if (!LevelUtil.IsBuildingStory(level))
                  {
                     if (prevBuildingStorey == null)
                        unassignedBaseLevels.Add(level);
                     else
                     {
                        levelInfo = IFCLevelInfo.Create(prevBuildingStorey, prevPlacement, prevHeight, prevElev, lengthScale, true);
                        ExporterCacheManager.LevelInfoCache.AddLevelInfo(level, levelInfo, false);
                     }
                     continue;
                  }

                  if (views.TryGetValue(level.Id, out View view) && view != null)
                  {
                     ExporterCacheManager.DBViewsToExport[view.Id] = level.Id;
                  }

                  double elev = level.ProjectElevation;
                  double height = 0.0;
                  List<ElementId> coincidentLevels = new List<ElementId>();
                  for (int jj = ii + 1; jj < allLevels.Count; jj++)
                  {
                     Level nextLevel = allLevels[jj];
                     if (!LevelUtil.IsBuildingStory(nextLevel))
                        continue;

                     double nextElev = nextLevel.ProjectElevation;
                     if (!MathUtil.IsAlmostEqual(nextElev, elev))
                     {
                        height = nextElev - elev;
                        break;
                     }
                     else if (ExporterCacheManager.ExportOptionsCache.WallAndColumnSplitting)
                        coincidentLevels.Add(nextLevel.Id);
                  }

                  double elevation = UnitUtil.ScaleLength(elev);
                  XYZ orig = new(0.0, 0.0, elevation);

                  IFCAnyHandle placement = ExporterUtil.CreateLocalPlacement(file, buildingPlacement, orig, null, null);
                  string bsObjectType = NamingUtil.GetObjectTypeOverride(level, null);
                  IFCElementComposition ifcComposition = LevelUtil.GetElementCompositionTypeOverride(level);

                  // IFC4.3 questions: How do we best support predefined type for IfcFacilityParts other than
                  // IfcBuildingStoreys?
                  // Are nested IfcFacilityParts more common/needed, or is that an OK limitation for Revit to only
                  // have one level supported?
                  // Do we need to sort by elevation, even though elevation is only for building stories?
                  string predefinedType = null;
                  IFCAnyHandle facilityPart = CreateFacilityPart(exporterIFC, level, bsObjectType, placement, 
                     ifcComposition, elevation, predefinedType);

                  // Create classification reference when level has classification field name assigned to it
                  ClassificationUtil.CreateClassification(exporterIFC, file, level, facilityPart);

                  prevBuildingStorey = facilityPart;
                  prevPlacement = placement;
                  prevHeight = height;
                  prevElev = elev;

                  levelInfo = IFCLevelInfo.Create(facilityPart, placement, height, elev, lengthScale, true);
                  ExporterCacheManager.LevelInfoCache.AddLevelInfo(level, levelInfo, true);

                  // if we have coincident levels, add buildingstories for them but use the old handle.
                  for (int jj = 0; jj < coincidentLevels.Count; jj++)
                  {
                     level = allLevels[ii + jj + 1];
                     levelInfo = IFCLevelInfo.Create(facilityPart, placement, height, elev, lengthScale, true);
                     ExporterCacheManager.LevelInfoCache.AddLevelInfo(level, levelInfo, true);
                  }

                  ii += coincidentLevels.Count;

                  // We will export element properties, quantities and classifications when we decide to keep the level - we may delete it later.
               }

               // Do a second pass to add level remapping, if any.
               foreach (Level level in allLevels)
               {
                  if (level == null)
                     continue;

                  ExporterCacheManager.LevelInfoCache.AddLevelRemapping(level);
               }
            }

            transaction.Commit();
         }
      }

      private void GetElementHandles(ICollection<ElementId> ids, ISet<IFCAnyHandle> handles)
      {
         if (ids != null)
         {
            foreach (ElementId id in ids)
            {
               handles.AddIfNotNull(ExporterCacheManager.ElementToHandleCache.Find(id));
            }
         }
      }

      private static IFCAnyHandle CreateRelServicesBuildings(IFCAnyHandle buildingHandle, IFCFile file,
         IFCAnyHandle ownerHistory, IFCAnyHandle systemHandle)
      {
         HashSet<IFCAnyHandle> relatedBuildings = new HashSet<IFCAnyHandle>() { buildingHandle };
         string guid = GUIDUtil.GenerateIFCGuidFrom(
            GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelServicesBuildings, systemHandle));
         return IFCInstanceExporter.CreateRelServicesBuildings(file, guid,
            ownerHistory, null, null, systemHandle, relatedBuildings);
      }

      private static void UpdateLocalPlacementForElement(IFCAnyHandle elemHnd, IFCFile file,
         IFCAnyHandle containerObjectPlacement, Transform containerInvTrf)
      {
         IFCAnyHandle elemObjectPlacementHnd = IFCAnyHandleUtil.GetObjectPlacement(elemHnd);

         // In the case that the object has no local placement at all.  In that case, create a new default one, and set the object's
         // local placement relative to the containerObjectPlacement.  Note that in this case we are ignoring containerInvTrf
         // entirely, which may not be the right thing to do, but doesn't currently seem to occur in practice.
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(elemObjectPlacementHnd) || !elemObjectPlacementHnd.IsTypeOf("IfcLocalPlacement"))
         {
            IFCAnyHandle relToContainerPlacement =
               ExporterUtil.CreateLocalPlacement(file, containerObjectPlacement, null, null, null);
            IFCAnyHandleUtil.SetAttribute(elemHnd, "ObjectPlacement", relToContainerPlacement);
            return;
         }

         // There are two cases here.
         // 1. We want to update the local placement of the object to be relative to its container without
         // adjusting its global position.  In this case containerInvTrf would be non-null, and we would
         // adjust the relative placement to keep the global position constant.
         // 2. We want to update the local placement of the object to follow any shift of the parent object.
         // In this case containerInvTrf would be null, and we don't update the relative placement.
         Transform newTrf = null;
         if (containerInvTrf != null)
         {
            IFCAnyHandle oldRelativePlacement = IFCAnyHandleUtil.GetInstanceAttribute(elemObjectPlacementHnd, "PlacementRelTo");
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(oldRelativePlacement))
            {
               newTrf = ExporterUtil.GetTransformFromLocalPlacementHnd(elemObjectPlacementHnd, false);
            }
            else
            {
               Transform originalTotalTrf = ExporterUtil.GetTotalTransformFromLocalPlacement(elemObjectPlacementHnd);
               newTrf = containerInvTrf.Multiply(originalTotalTrf);
            }
         }

         GeometryUtil.SetPlacementRelTo(elemObjectPlacementHnd, containerObjectPlacement);

         if (newTrf == null)
            return;

         IFCAnyHandle newRelativePlacement =
            ExporterUtil.CreateAxis2Placement3D(file, newTrf.Origin, newTrf.BasisZ, newTrf.BasisX);
         GeometryUtil.SetRelativePlacement(elemObjectPlacementHnd, newRelativePlacement);
      }

      private void CreatePresentationLayerAssignments(ExporterIFC exporterIFC, IFCFile file)
      {
         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x2 || ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly)
            return;

         ISet<IFCAnyHandle> assignedRepresentations = new HashSet<IFCAnyHandle>();
         IDictionary<string, ISet<IFCAnyHandle>> combinedPresentationLayerSet =
            new Dictionary<string, ISet<IFCAnyHandle>>();

         foreach (KeyValuePair<string, ICollection<IFCAnyHandle>> presentationLayerSet in ExporterCacheManager.PresentationLayerSetCache)
         {
            ISet<IFCAnyHandle> validHandles = new HashSet<IFCAnyHandle>();
            foreach (IFCAnyHandle handle in presentationLayerSet.Value)
            {
               if (ExporterCacheManager.HandleToDeleteCache.Contains(handle))
                  continue;

               if (validHandles.AddIfNotNull(handle))
               {
                  assignedRepresentations.Add(handle);
               }
            }

            if (validHandles.Count == 0)
               continue;

            combinedPresentationLayerSet[presentationLayerSet.Key] = validHandles;
         }

         // Now handle the internal cases.
         IDictionary<string, IList<IFCAnyHandle>> presentationLayerAssignments = exporterIFC.GetPresentationLayerAssignments();
         foreach (KeyValuePair<string, IList<IFCAnyHandle>> presentationLayerAssignment in presentationLayerAssignments)
         {
            // Some of the items may have been deleted, remove them from set.
            ICollection<IFCAnyHandle> newLayeredItemSet = new HashSet<IFCAnyHandle>();
            IList<IFCAnyHandle> initialSet = presentationLayerAssignment.Value;
            foreach (IFCAnyHandle currItem in initialSet)
            {
               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(currItem) && !assignedRepresentations.Contains(currItem))
                  newLayeredItemSet.Add(currItem);
            }

            if (newLayeredItemSet.Count == 0)
               continue;

            string layerName = presentationLayerAssignment.Key;
            ISet<IFCAnyHandle> layeredItemSet;
            if (!combinedPresentationLayerSet.TryGetValue(layerName, out layeredItemSet))
            {
               layeredItemSet = new HashSet<IFCAnyHandle>();
               combinedPresentationLayerSet[layerName] = layeredItemSet;
            }
            layeredItemSet.UnionWith(newLayeredItemSet);
         }

         foreach (KeyValuePair<string, ISet<IFCAnyHandle>> presentationLayerSet in combinedPresentationLayerSet)
         {
            IFCInstanceExporter.CreatePresentationLayerAssignment(file, presentationLayerSet.Key, null, presentationLayerSet.Value, null);
         }
      }

      private void OverrideOneGUID(IFCAnyHandle handle, IDictionary<string, int> indexMap, 
         Document document, ElementId elementId)
      {
         string typeName = handle.TypeName;
         Element element = document.GetElement(elementId);
         if (element == null)
            return;

         if (!indexMap.TryGetValue(typeName, out int index))
            index = 1;

         indexMap[typeName] = index+1;
         string globalId = GUIDUtil.GenerateIFCGuidFrom(
            GUIDUtil.CreateGUIDString(element, "Internal: " + typeName + index.ToString()));
         ExporterUtil.SetGlobalId(handle, globalId);
      }

      private void EndHostDocumentExport(ExporterIFC exporterIFC, Document document)
      {
         EndDocumentExportCommon(exporterIFC, document, false);
      }

      private void EndLinkedDocumentExport(ExporterIFC exporterIFC, Document document,
         Transform linkTrf)
      {
         EndDocumentExportCommon(exporterIFC, document, true);

         bool canUseSitePlacement = 
            ExporterCacheManager.ExportOptionsCache.ExportLinkedFileAs == LinkedFileExportAs.ExportSameProject;
         IFCAnyHandle topHandle = canUseSitePlacement ? 
            ExporterCacheManager.SiteExportInfo.SiteHandle : ExporterCacheManager.BuildingHandle;
         OrientLink(exporterIFC.GetFile(), canUseSitePlacement, 
            IFCAnyHandleUtil.GetObjectPlacement(topHandle), linkTrf);
      }

      /// <summary>
      /// Completes the export process by writing information stored incrementally during export to the file.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="document">The document to export.</param>
      /// <param name="exportingLink">True if we are exporting a link.</param>
      private void EndDocumentExportCommon(ExporterIFC exporterIFC, Document document,
         bool exportingLink)
      {
         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            // Relate Ducts and Pipes to their coverings (insulations and linings)
            foreach (KeyValuePair<ElementId, ElementId> ductOrPipe in ExporterCacheManager.MEPCache.CoveredElementsCache)
            {
               ElementId ductOrPipeId = ductOrPipe.Key;

               IFCAnyHandle ductOrPipeHandle = ExporterCacheManager.MEPCache.Find(ductOrPipeId);
               if (IFCAnyHandleUtil.IsNullOrHasNoValue(ductOrPipeHandle))
                  continue;

               HashSet<IFCAnyHandle> coveringHandles = new HashSet<IFCAnyHandle>();

               try
               {
                  if (FamilyInstanceExporter.CategoryCanHaveLining(ductOrPipe.Value))
                  {
                     ICollection<ElementId> liningIds = InsulationLiningBase.GetLiningIds(document, ductOrPipeId);
                     GetElementHandles(liningIds, coveringHandles);
                  }
               }
               catch
               {
               }

               try
               {
                  ICollection<ElementId> insulationIds = InsulationLiningBase.GetInsulationIds(document, ductOrPipeId);
                  GetElementHandles(insulationIds, coveringHandles);
               }
               catch
               {
               }

               if (coveringHandles.Count > 0)
               {
                  string relCoversGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelCoversBldgElements, ductOrPipeHandle));
                  IFCInstanceExporter.CreateRelCoversBldgElements(file, relCoversGuid, ownerHistory, null, null, ductOrPipeHandle, coveringHandles);
               }
            }

            // Relate stair components to stairs
            foreach (KeyValuePair<ElementId, StairRampContainerInfo> stairRamp in ExporterCacheManager.StairRampContainerInfoCache)
            {
               StairRampContainerInfo stairRampInfo = stairRamp.Value;

               IList<IFCAnyHandle> hnds = stairRampInfo.StairOrRampHandles;
               for (int ii = 0; ii < hnds.Count; ii++)
               {
                  IFCAnyHandle hnd = hnds[ii];
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(hnd))
                     continue;

                  IList<IFCAnyHandle> comps = stairRampInfo.Components[ii];
                  if (comps.Count == 0)
                     continue;

                  ExporterUtil.RelateObjects(exporterIFC, null, hnd, comps);
               }
            }

            // create a Default site if we have latitude and longitude information.
            if (!ExporterCacheManager.SiteExportInfo.IsSiteExported())
            {
               using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
               {
                  SiteExporter.ExportDefaultSite(exporterIFC, document, productWrapper);
                  ExporterUtil.ExportRelatedProperties(exporterIFC, document.ProjectInformation, productWrapper);
               }
            }

            ProjectInfo projectInfo = document.ProjectInformation;

            IFCAnyHandle projectHandle = ExporterCacheManager.ProjectHandle;
            IFCAnyHandle siteHandle = ExporterCacheManager.SiteExportInfo.SiteHandle;
            IFCAnyHandle facilityHandle = ExporterCacheManager.BuildingHandle;

            bool projectHasSite = !IFCAnyHandleUtil.IsNullOrHasNoValue(siteHandle);
            bool projectHasFacility = !IFCAnyHandleUtil.IsNullOrHasNoValue(facilityHandle);

            if (!projectHasSite && !projectHasFacility)
            {
               // if at this point the facilityHnd is null, which means that the model does not
               // have Site nor any Level assigned to the FacilityPart, create the IfcFacility 
               // as the general container for all the elements (should be backward compatible).
               IFCAnyHandle facilityPlacement = CreateBuildingPlacement(file);
               facilityHandle = CreateFacilityFromProjectInfo(exporterIFC, document, facilityPlacement, true);
               ExporterCacheManager.BuildingHandle = facilityHandle;
               projectHasFacility = true;
            }

            IFCAnyHandle siteOrFacilityHnd = projectHasFacility ? facilityHandle : siteHandle;

            // Last chance to create the building handle was just above.
            if (projectHasSite)
            {
               // Don't add the relation if we've already created it, which is if we are
               // exporting a linked file in a federated export while we are sharing the site.
               if (!exportingLink ||
                  ExporterCacheManager.ExportOptionsCache.ExportLinkedFileAs != LinkedFileExportAs.ExportSameSite)
               {
                  ExporterCacheManager.ContainmentCache.AddRelation(projectHandle, siteHandle);
               }

               if (projectHasFacility)
               {
                  // assoc. site to the facility.
                  ExporterCacheManager.ContainmentCache.AddRelation(siteHandle, facilityHandle);

                  IFCAnyHandle buildingPlacement = IFCAnyHandleUtil.GetObjectPlacement(facilityHandle);
                  IFCAnyHandle relPlacement = IFCAnyHandleUtil.GetObjectPlacement(siteHandle);
                  GeometryUtil.SetPlacementRelTo(buildingPlacement, relPlacement);
               }
            }
            else
            {
               // relate building and project if no site
               if (projectHasFacility)
                  ExporterCacheManager.ContainmentCache.AddRelation(projectHandle, facilityHandle);
            }

            // relate assembly elements to assemblies
            foreach (KeyValuePair<ElementId, AssemblyInstanceInfo> assemblyInfoEntry in ExporterCacheManager.AssemblyInstanceCache)
            {
               AssemblyInstanceInfo assemblyInfo = assemblyInfoEntry.Value;
               if (assemblyInfo == null)
                  continue;

               IFCAnyHandle assemblyInstanceHandle = assemblyInfo.AssemblyInstanceHandle;
               HashSet<IFCAnyHandle> elementHandles = assemblyInfo.ElementHandles;
               if (elementHandles != null && assemblyInstanceHandle != null && elementHandles.Contains(assemblyInstanceHandle))
                  elementHandles.Remove(assemblyInstanceHandle);

               if (assemblyInstanceHandle != null && elementHandles != null && elementHandles.Count != 0)
               {
                  Element assemblyInstance = document.GetElement(assemblyInfoEntry.Key);
                  string guid = GUIDUtil.CreateSubElementGUID(assemblyInstance, (int)IFCAssemblyInstanceSubElements.RelContainedInSpatialStructure);

                  if (IFCAnyHandleUtil.IsSubTypeOf(assemblyInstanceHandle, IFCEntityType.IfcSystem))
                  {
                     IFCInstanceExporter.CreateRelAssignsToGroup(file, guid, ownerHistory, null, null, elementHandles, null, assemblyInstanceHandle);
                  }
                  else
                  {
                     ExporterUtil.RelateObjects(exporterIFC, guid, assemblyInstanceHandle, elementHandles);
                     // Set the PlacementRelTo of assembly elements to assembly instance.
                     IFCAnyHandle assemblyPlacement = IFCAnyHandleUtil.GetObjectPlacement(assemblyInstanceHandle);
                     AssemblyInstanceExporter.SetLocalPlacementsRelativeToAssembly(exporterIFC, assemblyPlacement, elementHandles);
                  }

                  // We don't do this in RegisterAssemblyElement because we want to make sure that the IfcElementAssembly has been created.
                  ExporterCacheManager.ElementsInAssembliesCache.UnionWith(elementHandles);
               }
            }

            // Relate levels and products.  This may create new orphaned elements, so deal with those next.
            RelateLevels(exporterIFC, document);

            IFCAnyHandle defContainerObjectPlacement = IFCAnyHandleUtil.GetObjectPlacement(siteOrFacilityHnd);
            Transform defContainerTrf = ExporterUtil.GetTotalTransformFromLocalPlacement(defContainerObjectPlacement);
            Transform defContainerInvTrf = defContainerTrf.Inverse;

            // create an association between the IfcBuilding and building elements with no other containment.
            HashSet<IFCAnyHandle> buildingElements = RemoveContainedHandlesFromSet(ExporterCacheManager.LevelInfoCache.OrphanedElements);
            buildingElements.UnionWith(exporterIFC.GetRelatedElements());
            if (buildingElements.Count > 0)
            {
               HashSet<IFCAnyHandle> relatedElementSetForSite = new HashSet<IFCAnyHandle>();
               HashSet<IFCAnyHandle> relatedElementSetForBuilding = new HashSet<IFCAnyHandle>();
               // If the object is supposed to be placed directly on Site or Building, change the object placement to be relative to the Site or Building
               foreach (IFCAnyHandle elemHnd in buildingElements)
               {
                  ElementId elementId = ExporterCacheManager.HandleToElementCache.Find(elemHnd);
                  Element elem = document.GetElement(elementId);

                  // if there is override, use the override otherwise use default
                  IFCAnyHandle overrideContainer = null;
                  ParameterUtil.OverrideContainmentParameter(elem, out overrideContainer);

                  bool containerIsSite = projectHasSite;
                  bool containerIsFacility = projectHasFacility;

                  IFCAnyHandle containerObjectPlacement = null;
                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(overrideContainer))
                  {
                     containerObjectPlacement = IFCAnyHandleUtil.GetObjectPlacement(overrideContainer);
                     containerIsSite = IFCAnyHandleUtil.IsTypeOf(overrideContainer, IFCEntityType.IfcSite);
                     containerIsFacility = !containerIsSite &&
                        IFCAnyHandleUtil.IsTypeOf(overrideContainer, IFCEntityType.IfcBuilding);
                  }
                  else
                  {
                     // Default behavior (generally facility).
                     containerObjectPlacement = defContainerObjectPlacement;
                  }

                  if (containerIsFacility)
                     relatedElementSetForBuilding.Add(elemHnd);
                  else if (containerIsSite)
                     relatedElementSetForSite.Add(elemHnd);
                  
                  UpdateLocalPlacementForElement(elemHnd, file, containerObjectPlacement, null);
               }

               if (relatedElementSetForBuilding.Count > 0 && projectHasFacility)
               {
                  string guid = GUIDUtil.CreateSubElementGUID(projectInfo, (int)IFCProjectSubElements.RelContainedInBuildingSpatialStructure);
                  IFCInstanceExporter.CreateRelContainedInSpatialStructure(file, guid,
                     ownerHistory, null, null, relatedElementSetForBuilding, facilityHandle);
               }

               if (relatedElementSetForSite.Count > 0 && projectHasSite)
               {
                  string guid = GUIDUtil.CreateSubElementGUID(projectInfo, (int)IFCProjectSubElements.RelContainedInSiteSpatialStructure);
                  IFCInstanceExporter.CreateRelContainedInSpatialStructure(file, guid,
                     ownerHistory, null, null, relatedElementSetForSite, siteHandle);
               }
            }

            // create an association between the IfcBuilding and spacial elements with no other containment
            // The name "GetRelatedProducts()" is misleading; this only covers spaces.
            HashSet<IFCAnyHandle> buildingSpaces = RemoveContainedHandlesFromSet(ExporterCacheManager.LevelInfoCache.OrphanedSpaces);
            buildingSpaces.UnionWith(exporterIFC.GetRelatedProducts());
            if (buildingSpaces.Count > 0)
            {
               HashSet<IFCAnyHandle> relatedElementSetForBuilding = new HashSet<IFCAnyHandle>();
               HashSet<IFCAnyHandle> relatedElementSetForSite = new HashSet<IFCAnyHandle>();
               foreach (IFCAnyHandle indivSpace in buildingSpaces)
               {
                  bool containerIsSite = projectHasSite;
                  bool containerIsBuilding = projectHasFacility;

                  // if there is override, use the override otherwise use default
                  IFCAnyHandle overrideContainer = null;
                  ParameterUtil.OverrideSpaceContainmentParameter(document, indivSpace, out overrideContainer);
                  IFCAnyHandle containerObjectPlacement = null;
                  Transform containerInvTrf = null;

                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(overrideContainer))
                  {
                     containerObjectPlacement = IFCAnyHandleUtil.GetObjectPlacement(overrideContainer);
                     Transform containerTrf = ExporterUtil.GetTotalTransformFromLocalPlacement(containerObjectPlacement);
                     containerInvTrf = containerTrf.Inverse;
                     containerIsSite = IFCAnyHandleUtil.IsTypeOf(overrideContainer, IFCEntityType.IfcSite);
                     containerIsBuilding = !containerIsSite &&
                        IFCAnyHandleUtil.IsTypeOf(overrideContainer, IFCEntityType.IfcBuilding);
                  }
                  else
                  {
                     // Default behavior (generally facility).
                     containerObjectPlacement = defContainerObjectPlacement;
                     containerInvTrf = defContainerInvTrf;
                  }

                  if (containerIsBuilding)
                     relatedElementSetForBuilding.Add(indivSpace);
                  else if (containerIsSite)
                     relatedElementSetForSite.Add(indivSpace);

                  UpdateLocalPlacementForElement(indivSpace, file, containerObjectPlacement, containerInvTrf);
               }

               if (relatedElementSetForBuilding.Count > 0)
               {
                  ExporterCacheManager.ContainmentCache.AddRelations(facilityHandle, null, relatedElementSetForBuilding);
               }

               if (relatedElementSetForSite.Count > 0)
               {
                  ExporterCacheManager.ContainmentCache.AddRelations(siteHandle, null, relatedElementSetForSite);
               }
            }

            // relate objects in containment cache.
            foreach (KeyValuePair<IFCAnyHandle, HashSet<IFCAnyHandle>> container in ExporterCacheManager.ContainmentCache.Cache)
            {
               if (container.Value.Count() > 0)
               {
                  string relationGUID = ExporterCacheManager.ContainmentCache.GetGUIDForRelation(container.Key);
                  ExporterUtil.RelateObjects(exporterIFC, relationGUID, container.Key, container.Value);
               }
            }

            // These elements are created internally, but we allow custom property sets for them.  Create them here.
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               if (projectHasFacility)
                  productWrapper.AddBuilding(projectInfo, facilityHandle);
               if (projectInfo != null)
                  ExporterUtil.ExportRelatedProperties(exporterIFC, projectInfo, productWrapper);
            }

            // TODO: combine all of the various material paths and caches; they are confusing and
            // prone to error.

            // create material layer associations
            foreach (KeyValuePair<IFCAnyHandle, ISet<IFCAnyHandle>> materialSetLayerUsage in ExporterCacheManager.MaterialSetUsageCache.Cache)
            {
               if ((materialSetLayerUsage.Value?.Count ?? 0) == 0)
                  continue;

               string guid = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesMaterial, ExporterUtil.GetGlobalId(materialSetLayerUsage.Value.First())));
               IFCInstanceExporter.CreateRelAssociatesMaterial(file, guid, ownerHistory,
                  null, null, materialSetLayerUsage.Value,
                  materialSetLayerUsage.Key);
            }

            // create material constituent set associations
            foreach (KeyValuePair<IFCAnyHandle, ISet<IFCAnyHandle>> relAssoc in ExporterCacheManager.MaterialConstituentSetCache.Cache)
            {
               if (IFCAnyHandleUtil.IsNullOrHasNoValue(relAssoc.Key))
                  continue;

               ISet<IFCAnyHandle> relatedObjects = ExporterUtil.CleanRefObjects(relAssoc.Value);
               if ((relatedObjects?.Count ?? 0) == 0)
                  continue;

               // TODO_GUID: relAssoc.Value.First() is somewhat stable, as long as the objects
               // always come in the same order and elements aren't deleted.
               string guid = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesMaterial, relAssoc.Value.First()));
               IFCInstanceExporter.CreateRelAssociatesMaterial(file, guid, ownerHistory,
                  null, null, relatedObjects, relAssoc.Key);
            }

            // Update the GUIDs for internally created IfcRoot entities, if any.
            // This is a little expensive because of the inverse attribute workaround, so we only
            // do it if any internal handles were created.
            if (ExporterCacheManager.InternallyCreatedRootHandles.Count > 0)
            {
               // Patch IfcOpeningElement, IfcRelVoidsElement, IfcElementQuantity, and
               // IfcRelDefinesByProperties GUIDs created by internal code.  This code is
               // written specifically for this case, but could be easily generalized.

               IDictionary<long, ISet<IFCAnyHandle>> elementIdToHandles = 
                  new SortedDictionary<long, ISet<IFCAnyHandle>>();

               // For some reason, the inverse attribute isn't working.  So we need to get
               // the IfcRelVoidElements and IfcRelDefinesByProperties and effectively create
               // the inverse cache, but only if there were any internally created root handles.

               // First, look at all IfcRelVoidElements.
               IList<IFCAnyHandle> voids = exporterIFC.GetFile().GetInstances(IFCEntityType.IfcRelVoidsElement.ToString(), false);
               foreach (IFCAnyHandle voidHandle in voids)
               {
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(voidHandle))
                     continue;

                  IFCAnyHandle openingHandle = IFCAnyHandleUtil.GetInstanceAttribute(voidHandle, "RelatedOpeningElement");
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(openingHandle))
                     continue;

                  if (!ExporterCacheManager.InternallyCreatedRootHandles.TryGetValue(openingHandle, out ElementId elementId))
                     continue;

                  long elementIdVal = elementId.Value;
                  if (!elementIdToHandles.TryGetValue(elementIdVal, out ISet<IFCAnyHandle> internalHandes))
                  {
                     internalHandes = new SortedSet<IFCAnyHandle>(new BaseRelationsCache.IFCAnyHandleComparer());
                     elementIdToHandles[elementIdVal] = internalHandes;
                  }

                  internalHandes.Add(voidHandle); // IfcRelVoidsElement
               }

               // Second, look at all IfcRelDefinesByProperties.
               IList<IFCAnyHandle> relProperties = exporterIFC.GetFile().GetInstances(IFCEntityType.IfcRelDefinesByProperties.ToString(), false);
               foreach (IFCAnyHandle relPropertyHandle in relProperties)
               {
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(relPropertyHandle))
                     continue;

                  ICollection<IFCAnyHandle> relatedHandles = IFCAnyHandleUtil.GetAggregateInstanceAttribute<List<IFCAnyHandle>>(relPropertyHandle, "RelatedObjects");
                  if ((relatedHandles?.Count ?? 0) == 0)
                     continue;

                  foreach (IFCAnyHandle relatedHandle in relatedHandles)
                  {
                     if (!ExporterCacheManager.InternallyCreatedRootHandles.TryGetValue(relatedHandle, out ElementId elementId))
                        continue;

                     long elementIdVal = elementId.Value;
                     if (!elementIdToHandles.TryGetValue(elementIdVal, out ISet<IFCAnyHandle> internalHandes))
                     {
                        internalHandes = new SortedSet<IFCAnyHandle>(new BaseRelationsCache.IFCAnyHandleComparer());
                        elementIdToHandles[elementIdVal] = internalHandes;
                     }

                     IFCAnyHandle propertyHandle = IFCAnyHandleUtil.GetInstanceAttribute(relPropertyHandle, "RelatingPropertyDefinition");
                     internalHandes.Add(propertyHandle); // IfcQuantitySet
                     internalHandes.Add(relPropertyHandle); // IfcRelDefinesByProperties.
                     break;
                  }
               }

               IDictionary<string, int> indexMap = new Dictionary<string, int>();

               foreach (KeyValuePair<long, ISet<IFCAnyHandle>> handleSets in elementIdToHandles)
               {
                  ElementId elementId = new ElementId(handleSets.Key);
                  foreach (IFCAnyHandle handle in handleSets.Value)
                  {
                     OverrideOneGUID(handle, indexMap, document, elementId);
                  }
               }
            }

            // For some older Revit files, we may have the same material name used for multiple
            // elements.  Without doing a significant amount of rewrite that likely isn't worth
            // the effort, we can at least create a cache here of used names with indices so that
            // we don't duplicate the GUID while still reusing the name to match the model.
            IDictionary<string, int> UniqueMaterialNameCache = new Dictionary<string, int>();

            // create material associations
            foreach (IFCAnyHandle materialHnd in ExporterCacheManager.MaterialRelationsCache.Keys)
            {
               // In some specific cased the reference object might have been deleted. Clear those from the Type cache first here
               ISet<IFCAnyHandle> materialRelationsHandles = ExporterCacheManager.MaterialRelationsCache.CleanRefObjects(materialHnd);
               if ((materialRelationsHandles?.Count ?? 0) > 0)
               {
                  string materialName = GetMaterialNameFromMaterialSelect(materialHnd);
                  
                  string guidHash = materialName;
                  if (UniqueMaterialNameCache.TryGetValue(guidHash, out int index))
                  {
                     UniqueMaterialNameCache[guidHash]++;
                     // In theory this could be a duplicate material name, but really highly
                     // unlikely.
                     guidHash += " GUID Copy: " + index.ToString();
                  }
                  else
                  {
                     UniqueMaterialNameCache[guidHash] = 2;
                  }

                  string guid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesMaterial, guidHash));
                  IFCInstanceExporter.CreateRelAssociatesMaterial(file, guid, ownerHistory,
                     null, null, materialRelationsHandles, materialHnd);
               }
            }

            // create type relations
            foreach (IFCAnyHandle typeObj in ExporterCacheManager.TypeRelationsCache.Keys)
            {
               // In some specific cased the reference object might have been deleted. Clear those from the Type cache first here
               ISet<IFCAnyHandle> typeRelCache = ExporterCacheManager.TypeRelationsCache.CleanRefObjects(typeObj);
               if ((typeRelCache?.Count ?? 0) > 0)
               {
                  string guid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelDefinesByType, typeObj));
                  IFCInstanceExporter.CreateRelDefinesByType(file, guid, ownerHistory, 
                     null, null, typeRelCache, typeObj);
               }
            }

            // Create property set relations.
            ExporterCacheManager.CreatedInternalPropertySets.CreateRelations(file);

            ExporterCacheManager.CreatedSpecialPropertySets.CreateRelations(file);

            // Create type property relations.
            foreach (TypePropertyInfo typePropertyInfo in ExporterCacheManager.TypePropertyInfoCache.Values)
            {
               if (typePropertyInfo.AssignedToType)
                  continue;

               ICollection<IFCAnyHandle> propertySets = typePropertyInfo.PropertySets;
               ISet<IFCAnyHandle> elements = typePropertyInfo.Elements;

               if (elements.Count == 0)
                  continue;

               foreach (IFCAnyHandle propertySet in propertySets)
               {
                  try
                  {
                     ExporterUtil.CreateRelDefinesByProperties(file,
                        ownerHistory, null, null, elements, propertySet);
                  }
                  catch
                  {
                  }
               }
            }

            // create space boundaries
            foreach (SpaceBoundary boundary in ExporterCacheManager.SpaceBoundaryCache)
            {
               SpatialElementExporter.ProcessIFCSpaceBoundary(exporterIFC, boundary, file);
            }

            // create wall/wall connectivity objects
            if (ExporterCacheManager.WallConnectionDataCache.Count > 0 && !ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
            {
               IList<IDictionary<ElementId, IFCAnyHandle>> hostObjects = exporterIFC.GetHostObjects();
               List<int> relatingPriorities = new List<int>();
               List<int> relatedPriorities = new List<int>();

               string ifcElementEntityType = IFCEntityType.IfcElement.ToString();

               foreach (WallConnectionData wallConnectionData in ExporterCacheManager.WallConnectionDataCache)
               {
                  foreach (IDictionary<ElementId, IFCAnyHandle> mapForLevel in hostObjects)
                  {
                     IFCAnyHandle wallElementHandle, otherElementHandle;
                     if (!mapForLevel.TryGetValue(wallConnectionData.FirstId, out wallElementHandle))
                        continue;
                     if (!mapForLevel.TryGetValue(wallConnectionData.SecondId, out otherElementHandle))
                        continue;

                     if (!wallElementHandle.IsSubTypeOf(ifcElementEntityType) ||
                        !otherElementHandle.IsSubTypeOf(ifcElementEntityType))
                        continue;

                     // NOTE: Definition of RelConnectsPathElements has the connection information reversed
                     // with respect to the order of the paths.
                     string connectionName = ExporterUtil.GetGlobalId(wallElementHandle) + "|" + 
                        ExporterUtil.GetGlobalId(otherElementHandle);
                     string relGuid = GUIDUtil.GenerateIFCGuidFrom(
                        GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelConnectsPathElements,
                           connectionName + ":" + 
                           wallConnectionData.SecondConnectionType.ToString() + ":" +
                           wallConnectionData.FirstConnectionType.ToString()));
                     const string connectionType = "Structural";   // Assigned as Description
                     IFCInstanceExporter.CreateRelConnectsPathElements(file, relGuid, ownerHistory,
                        connectionName, connectionType, wallConnectionData.ConnectionGeometry, 
                        wallElementHandle, otherElementHandle, relatingPriorities,
                        relatedPriorities, wallConnectionData.SecondConnectionType, 
                        wallConnectionData.FirstConnectionType);
                  }
               }
            }

            // create Zones and groups of Zones.
            {
               // Collect zone group names as we go.  We will limit a zone to be only in one group.
               IDictionary<string, ISet<IFCAnyHandle>> zoneGroups = new Dictionary<string, ISet<IFCAnyHandle>>();

               string relAssignsToGroupName = "Spatial Zone Assignment";
               foreach (KeyValuePair<string, ZoneInfo> zone in ExporterCacheManager.ZoneInfoCache)
               {
                  ZoneInfo zoneInfo = zone.Value;
                  if (zoneInfo == null)
                     continue;

                  string zoneName = zone.Key;
                  string zoneGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcZone, zone.Key));
                  IFCAnyHandle zoneHandle = IFCInstanceExporter.CreateZone(file, zoneGuid, ownerHistory,
                      zoneName, zoneInfo.Description, zoneInfo.ObjectType, zoneInfo.LongName);
                  string groupGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, zoneName, zoneHandle));
                  IFCInstanceExporter.CreateRelAssignsToGroup(file, groupGuid, ownerHistory,
                      relAssignsToGroupName, null, zoneInfo.RoomHandles, null, zoneHandle);

                  HashSet<IFCAnyHandle> zoneHnds = new HashSet<IFCAnyHandle>() { zoneHandle };
                  
                  foreach (KeyValuePair<string, IFCAnyHandle> classificationReference in zoneInfo.ClassificationReferences)
                  {
                     string relGuid = GUIDUtil.GenerateIFCGuidFrom(
                        GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesClassification,
                        classificationReference.Key, zoneHandle));
                     ExporterCacheManager.ClassificationCache.AddRelation(classificationReference.Value, 
                        relGuid, classificationReference.Key, null, zoneHnds);
                  }

                  IFCAnyHandle zoneCommonProperySetHandle = zoneInfo.CreateZoneCommonPSetData(file);
                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(zoneCommonProperySetHandle))
                  {
                     ExporterUtil.CreateRelDefinesByProperties(file,
                         ownerHistory, null, null, zoneHnds, zoneCommonProperySetHandle);
                  }

                  string groupName = zoneInfo.GroupName;
                  if (!string.IsNullOrWhiteSpace(groupName))
                  {
                     ISet<IFCAnyHandle> currentGroup = null;
                     if (!zoneGroups.TryGetValue(groupName, out currentGroup))
                     {
                        currentGroup = new HashSet<IFCAnyHandle>();
                        zoneGroups.Add(groupName, currentGroup);
                     }
                     currentGroup.Add(zoneHandle);
                  }
               }

               // now create any zone groups.
               string relAssignsToZoneGroupName = "Zone Group Assignment";
               foreach (KeyValuePair<string, ISet<IFCAnyHandle>> zoneGroup in zoneGroups)
               {
                  string zoneGroupGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcGroup, zoneGroup.Key));
                  IFCAnyHandle zoneGroupHandle = IFCInstanceExporter.CreateGroup(file,
                     zoneGroupGuid, ownerHistory, zoneGroup.Key, null, null);
                  string zoneGroupRelAssignsGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, zoneGroup.Key));
                  IFCInstanceExporter.CreateRelAssignsToGroup(file, zoneGroupRelAssignsGuid, 
                     ownerHistory, relAssignsToZoneGroupName, null, zoneGroup.Value, null, 
                     zoneGroupHandle);
               }
            }

            // create Space Occupants
            {
               foreach (string spaceOccupantName in ExporterCacheManager.SpaceOccupantInfoCache.Keys)
               {
                  SpaceOccupantInfo spaceOccupantInfo = ExporterCacheManager.SpaceOccupantInfoCache.Find(spaceOccupantName);
                  if (spaceOccupantInfo != null)
                  {
                     IFCAnyHandle person = IFCInstanceExporter.CreatePerson(file, null, spaceOccupantName, null, null, null, null, null, null);
                     string occupantGuid = GUIDUtil.GenerateIFCGuidFrom(
                        GUIDUtil.CreateGUIDString(IFCEntityType.IfcOccupant, spaceOccupantName));
                     IFCAnyHandle spaceOccupantHandle = IFCInstanceExporter.CreateOccupant(file,
                        occupantGuid, ownerHistory, null, null, spaceOccupantName, person, 
                        IFCOccupantType.NotDefined);

                     string relOccupiesSpacesGuid = GUIDUtil.GenerateIFCGuidFrom(
                        GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelOccupiesSpaces, spaceOccupantHandle));
                     IFCInstanceExporter.CreateRelOccupiesSpaces(file, relOccupiesSpacesGuid, 
                        ownerHistory, null, null, spaceOccupantInfo.RoomHandles, null, 
                        spaceOccupantHandle, null);

                     HashSet<IFCAnyHandle> spaceOccupantHandles = 
                        new HashSet<IFCAnyHandle>() { spaceOccupantHandle };

                     foreach (KeyValuePair<string, IFCAnyHandle> classificationReference in spaceOccupantInfo.ClassificationReferences)
                     {
                        string relGuid = GUIDUtil.GenerateIFCGuidFrom(
                           GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesClassification,
                           classificationReference.Key, spaceOccupantHandle));
                        ExporterCacheManager.ClassificationCache.AddRelation(classificationReference.Value,
                           relGuid, classificationReference.Key, null, spaceOccupantHandles);
                     }

                     if (spaceOccupantInfo.SpaceOccupantProperySetHandle != null && spaceOccupantInfo.SpaceOccupantProperySetHandle.HasValue)
                     {
                        ExporterUtil.CreateRelDefinesByProperties(file, 
                           ownerHistory, null, null, spaceOccupantHandles, spaceOccupantInfo.SpaceOccupantProperySetHandle);
                     }
                  }
               }
            }

            // Create systems.
            ExportCachedSystem(exporterIFC, document, file, ExporterCacheManager.SystemsCache.BuiltInSystemsCache, ownerHistory, facilityHandle, projectHasFacility, false);
            ExportCachedSystem(exporterIFC, document, file, ExporterCacheManager.SystemsCache.ElectricalSystemsCache, ownerHistory, facilityHandle, projectHasFacility, true);
            ExportCableTraySystem(document, file, ExporterCacheManager.MEPCache.CableElementsCache, ownerHistory, facilityHandle, projectHasFacility);

            // Add presentation layer assignments - this is in addition to those created internally.
            // Any representation in this list will override any internal assignment.
            CreatePresentationLayerAssignments(exporterIFC, file);

            // Add door/window openings.
            ExporterCacheManager.DoorWindowDelayedOpeningCreatorCache.ExecuteCreators(exporterIFC, document);

            foreach (SpaceInfo spaceInfo in ExporterCacheManager.SpaceInfoCache.SpaceInfos.Values)
            {
               if (spaceInfo.RelatedElements.Count > 0)
               {
                  string relContainedGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelContainedInSpatialStructure,
                     spaceInfo.SpaceHandle));
                  IFCInstanceExporter.CreateRelContainedInSpatialStructure(file, relContainedGuid, ownerHistory,
                      null, null, spaceInfo.RelatedElements, spaceInfo.SpaceHandle);
               }
            }

            // Create RelAssociatesClassifications.
            foreach (var relAssociatesInfo in ExporterCacheManager.ClassificationCache.ClassificationRelations)
            {
               if (IFCAnyHandleUtil.IsNullOrHasNoValue(relAssociatesInfo.Key))
                  continue;

               IFCInstanceExporter.CreateRelAssociatesClassification(file,
                  relAssociatesInfo.Value.GlobalId, ownerHistory,
                  relAssociatesInfo.Value.Name,
                  relAssociatesInfo.Value.Description,
                  relAssociatesInfo.Value.RelatedObjects,
                  relAssociatesInfo.Key);
            }

            // Delete handles that are marked for removal
            foreach (IFCAnyHandle handleToDel in ExporterCacheManager.HandleToDeleteCache)
            {
               handleToDel.Delete();
            }

            // Export material properties
            if (ExporterCacheManager.ExportOptionsCache.PropertySetOptions.ExportMaterialPsets)
               MaterialPropertiesUtil.ExportMaterialProperties(file, exporterIFC);

            // Create unit assignment
            IFCAnyHandle units = IFCInstanceExporter.CreateUnitAssignment(file, UnitMappingUtil.GetUnitsToAssign());
            ExporterCacheManager.ProjectHandle.SetAttribute("UnitsInContext", units);

            // Potentially modify elements with GUID values.
            if (ExporterCacheManager.GUIDsToStoreCache.Count > 0 &&
               ExporterUtil.ExportingHostModel())
            {
               using (SubTransaction st = new SubTransaction(document))
               {
                  st.Start();
                  foreach (KeyValuePair<(ElementId ElementId, BuiltInParameter ParamId), string> elementAndGUID in ExporterCacheManager.GUIDsToStoreCache)
                  {
                     Element element = document.GetElement(elementAndGUID.Key.ElementId);
                     if (element == null || elementAndGUID.Key.ParamId == BuiltInParameter.INVALID || elementAndGUID.Value == null)
                        continue;

                     ParameterUtil.SetStringParameter(element, elementAndGUID.Key.ParamId, elementAndGUID.Value);
                  }
                  st.Commit();
               }
            }

            // Allow native code to remove some unused handles and clear internal caches.
            ExporterIFCUtils.EndExportInternal(exporterIFC);
            transaction.Commit();
         }
      }

      private class IFCFileDocumentInfo
      {
         public string ContentGUIDString { get; private set; } = null;

         public string VersionGUIDString { get; private set; } = null;

         public int NumberOfSaves { get; private set; } = 0;

         public string ProjectNumber { get; private set; } = string.Empty;

         public string ProjectName { get; private set; } = string.Empty;

         public string ProjectStatus { get; private set; } = string.Empty;

         public string VersionName { get; private set; } = string.Empty;

         public string VersionBuild { get; private set; } = string.Empty;

         public string VersionBuildName { get; private set; } = string.Empty;

         public IFCFileDocumentInfo(Document document)
         {
            if (document == null)
               return;

            ProjectInfo projectInfo = document.ProjectInformation;
            DocumentVersion documentVersion = Document.GetDocumentVersion(document);
            Application application = document?.Application;

            ExportOptionsCache exportOptionsCache = ExporterCacheManager.ExportOptionsCache;

            ContentGUIDString = document?.CreationGUID.ToString() ?? string.Empty;
            VersionGUIDString = documentVersion?.VersionGUID.ToString() ?? string.Empty;
            NumberOfSaves = documentVersion?.NumberOfSaves ?? 0;

            ProjectNumber = projectInfo?.Number ?? string.Empty;
            ProjectName = projectInfo?.Name ?? exportOptionsCache.FileNameOnly;
            ProjectStatus = projectInfo?.Status ?? string.Empty;

            VersionName = application?.VersionName;
            VersionBuild = application?.VersionBuild;

            try
            {
               string productName = System.Diagnostics.FileVersionInfo.GetVersionInfo(application?.GetType().Assembly.Location).ProductName;
               VersionBuildName = productName + " " + VersionBuild;
            }
            catch
            { }
         }
      }

      private void OrientLink(IFCFile file, bool canUseSitePlacement, 
         IFCAnyHandle buildingOrSitePlacement, Transform linkTrf)
      {
         // When exporting Link, the relative position of the Link instance in the model needs to be transformed with
         // the offset from the main model site transform
         SiteTransformBasis transformBasis = ExporterCacheManager.ExportOptionsCache.SiteTransformation;
         bool useSitePlacement = canUseSitePlacement && (transformBasis != SiteTransformBasis.Internal);
         bool useRotation = transformBasis == SiteTransformBasis.InternalInTN ||
            transformBasis == SiteTransformBasis.ProjectInTN || 
            transformBasis == SiteTransformBasis.Shared || 
            transformBasis == SiteTransformBasis.Site;
         Transform sitePl = 
            new Transform(CoordReferenceInfo.MainModelCoordReferenceOffset ?? Transform.Identity);

         XYZ siteOffset = useSitePlacement ? sitePl.Origin : XYZ.Zero;
         if (useRotation && useSitePlacement)
         {
            // For those that oriented in the TN, a rotation is needed to compute a correct offset in TN orientation
            Transform rotationTrfAtInternal = Transform.CreateRotationAtPoint(XYZ.BasisZ, CoordReferenceInfo.MainModelTNAngle, XYZ.Zero);
            siteOffset += rotationTrfAtInternal.OfPoint(linkTrf.Origin);
         }
         else
         {
            siteOffset += linkTrf.Origin;
         }
         sitePl.Origin = XYZ.Zero;
         linkTrf.Origin = XYZ.Zero;
         Transform linkTotTrf = useSitePlacement ? sitePl.Multiply(linkTrf) : linkTrf;
         linkTotTrf.Origin = siteOffset;

         IFCAnyHandle relativePlacement = ExporterUtil.CreateAxis2Placement3D(file, 
            UnitUtil.ScaleLength(linkTotTrf.Origin), linkTotTrf.BasisZ, linkTotTrf.BasisX);

         // Note that we overwrite this here for subsequent writes, which clobbers the
         // original placement, so the IfcBuilding handle is suspect after this without
         // explicit cleanup.
         GeometryUtil.SetRelativePlacement(buildingOrSitePlacement, relativePlacement);
      }

      /// <summary>
      /// Write out the IFC file after all entities have been created.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="document">The document to export.</param>
      private void WriteIFCFile(IFCFile file, IFCFileDocumentInfo ifcFileDocumentInfo)
      {
         ifcFileDocumentInfo ??= new(null);

         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            //create header

            ExportOptionsCache exportOptionsCache = ExporterCacheManager.ExportOptionsCache;

            string coordinationView = null;
            if (exportOptionsCache.ExportAsCoordinationView2)
               coordinationView = "CoordinationView_V2.0";
            else
               coordinationView = "CoordinationView";

            List<string> descriptions = [];
            if (ExporterCacheManager.ExportOptionsCache.ExportAs2x2)
            {
               descriptions.Add("IFC2X_PLATFORM");
            }
            else if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
            {
               if (ExporterCacheManager.ExportOptionsCache.FileVersion == IFCVersion.IFCSG)
                  descriptions.Add("ViewDefinition [ReferenceView_V1.2, IFC-SG]");
               else
                  descriptions.Add("ViewDefinition [ReferenceView_V1.2]");
            }
            else if (ExporterCacheManager.ExportOptionsCache.ExportAs4DesignTransferView)
            {
               descriptions.Add("ViewDefinition [DesignTransferView_V1.0]");   // Tentative name as the standard is not yet fuly released
            }
            else
            {
               string currentLine;
               if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3COBIE24DesignDeliverable)
               {
                  currentLine = string.Format("ViewDefinition [{0}]",
                     "COBie2.4DesignDeliverable");
               }
               else
               {
                  currentLine = string.Format("ViewDefinition [{0}{1}]",
                     coordinationView,
                     ExporterCacheManager.ExportIFCBaseQuantities() ? ", QuantityTakeOffAddOnView" : "");
               }

               descriptions.Add(currentLine);

            }

            if (!ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly)
            {
               string versionLine = string.Format("RevitIdentifiers [ContentGUID: {0}, VersionGUID: {1}, NumberOfSaves: {2}]",
                  ifcFileDocumentInfo.ContentGUIDString, ifcFileDocumentInfo.VersionGUIDString, ifcFileDocumentInfo.NumberOfSaves);

               descriptions.Add(versionLine);

               if (!string.IsNullOrEmpty(ExporterCacheManager.ExportOptionsCache.ExcludeFilter))
               {
                  descriptions.Add("Options [Excluded Entities: " + ExporterCacheManager.ExportOptionsCache.ExcludeFilter + "]");
               }

               string projectNumber = ifcFileDocumentInfo?.ProjectNumber ?? string.Empty;
               string projectName = ifcFileDocumentInfo?.ProjectName ?? string.Empty;
               string projectStatus = ifcFileDocumentInfo?.ProjectStatus ?? string.Empty;

               IFCAnyHandle project = ExporterCacheManager.ProjectHandle;
               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(project))
                  IFCAnyHandleUtil.UpdateProject(project, projectNumber, projectName, projectStatus);
            }

            IFCInstanceExporter.CreateFileSchema(file);

            // Get stored File Header information from the UI and use it for export
            IFCFileHeaderItem fHItem = ExporterCacheManager.ExportOptionsCache.FileHeaderItem;

            // Add information in the File Description (e.g. Exchange Requirement) that is assigned in the UI
            if (fHItem.FileDescriptions.Count > 0)
               descriptions.AddRange(fHItem.FileDescriptions);
            IFCInstanceExporter.CreateFileDescription(file, descriptions);

            List<string> author = [];
            if (string.IsNullOrEmpty(fHItem.AuthorName) == false)
            {
               author.Add(fHItem.AuthorName);
               if (string.IsNullOrEmpty(fHItem.AuthorEmail) == false)
                  author.Add(fHItem.AuthorEmail);
            }
            else
               author.Add(string.Empty);

            List<string> organization = [];
            if (string.IsNullOrEmpty(fHItem.Organization) == false)
               organization.Add(fHItem.Organization);
            else
               organization.Add(string.Empty);

            LanguageType langType = ExporterCacheManager.LanguageType;
            string languageExtension = GetLanguageExtension(langType);
            string versionBuildName = ifcFileDocumentInfo.VersionBuildName;
            string versionInfos = versionBuildName + languageExtension + " - " +
               ExporterCacheManager.ExportOptionsCache.ExporterVersion;

            fHItem.Authorization ??= string.Empty;

            IFCInstanceExporter.CreateFileName(file, exportOptionsCache.FileNameOnly, author, organization,
               ifcFileDocumentInfo.VersionName, versionInfos, fHItem.Authorization);

            transaction.Commit();

            IFCFileWriteOptions writeOptions = new()
            {
               FileName = exportOptionsCache.FullFileName,
               FileFormat = exportOptionsCache.IFCFileFormat
            };

            // Reuse almost all of the information above to write out extra copies of the IFC file.
            if (exportOptionsCache.ExportingSeparateLink())
            {
               IFCAnyHandle buildingOrSiteHnd = ExporterCacheManager.BuildingHandle;
               if (IFCAnyHandleUtil.IsNullOrHasNoValue(buildingOrSiteHnd))
               {
                  buildingOrSiteHnd = ExporterCacheManager.SiteExportInfo.SiteHandle;
               }
               IFCAnyHandle buildingOrSitePlacement = IFCAnyHandleUtil.GetObjectPlacement(buildingOrSiteHnd);
               
               int numRevitLinkInstances = exportOptionsCache.GetNumLinkInstanceInfos();
               for (int ii = 0; ii < numRevitLinkInstances; ii++)
               {
                  // When exporting Link, the relative position of the Link instance in the model needs to be transformed with
                  // the offset from the main model site transform
                  Transform linkTrf = new Transform(ExporterCacheManager.ExportOptionsCache.GetUnscaledLinkInstanceTransform(ii));
                  OrientLink(file, true, buildingOrSitePlacement, linkTrf);
                  
                  string linkInstanceFileName = exportOptionsCache.GetLinkInstanceFileName(ii);
                  if (linkInstanceFileName != null)
                     writeOptions.FileName = linkInstanceFileName;

                  file.Write(writeOptions);
               }
            }
            else
            {
               file.Write(writeOptions);
            }

            // Display the message to the user when the IFC File has been completely exported 
            statusBar.Set(Resources.IFCExportComplete);
         }
      }

      private string GetLanguageExtension(LanguageType langType)
      {
         switch (langType)
         {
            case LanguageType.English_USA:
               return " (ENU)";
            case LanguageType.German:
               return " (DEU)";
            case LanguageType.Spanish:
               return " (ESP)";
            case LanguageType.French:
               return " (FRA)";
            case LanguageType.Italian:
               return " (ITA)";
            case LanguageType.Dutch:
               return " (NLD)";
            case LanguageType.Chinese_Simplified:
               return " (CHS)";
            case LanguageType.Chinese_Traditional:
               return " (CHT)";
            case LanguageType.Japanese:
               return " (JPN)";
            case LanguageType.Korean:
               return " (KOR)";
            case LanguageType.Russian:
               return " (RUS)";
            case LanguageType.Czech:
               return " (CSY)";
            case LanguageType.Polish:
               return " (PLK)";
            case LanguageType.Hungarian:
               return " (HUN)";
            case LanguageType.Brazilian_Portuguese:
               return " (PTB)";
            case LanguageType.English_GB:
               return " (ENG)";
            default:
               return "";
         }
      }

      private long GetCreationDate(Document document)
      {
         string pathName = document.PathName;
         // If this is not a locally saved file, we can't get the creation date.
         // This will require future work to get this, but it is very minor.
         DateTime creationTimeUtc = DateTime.Now;
         try
         {
            FileInfo fileInfo = new FileInfo(pathName);
            creationTimeUtc = fileInfo.CreationTimeUtc;
         }
         catch
         {
            creationTimeUtc = DateTime.Now;
         }

         // The IfcTimeStamp is measured in seconds since 1/1/1970.  As such, we divide by 10,000,000 
         // (100-ns ticks in a second) and subtract the 1/1/1970 offset.
         return creationTimeUtc.ToFileTimeUtc() / 10000000 - 11644473600;
      }

      /// <summary>
      /// Creates the application information.
      /// </summary>
      /// <param name="file">The IFC file.</param>
      /// <param name="app">The application object.</param>
      /// <returns>The handle of IFC file.</returns>
      private IFCAnyHandle CreateApplicationInformation(IFCFile file, Document document)
      {
         Application app = document.Application;
         string pathName = document.PathName;
         LanguageType langType = ExporterCacheManager.LanguageType;
         string languageExtension = GetLanguageExtension(langType);
         string productFullName = app.VersionName + languageExtension;
         string productVersion = app.VersionNumber;
         string productIdentifier = "Revit";

         IFCAnyHandle developer = IFCInstanceExporter.CreateOrganization(file, null, productFullName, null, null, null);
         IFCAnyHandle application = IFCInstanceExporter.CreateApplication(file, developer, productVersion, productFullName, productIdentifier);
         return application;
      }

      /// <summary>
      /// Creates the 3D and 2D contexts information.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="doc">The document provides the ProjectLocation.</param>
      /// <returns>The collection contains the 3D/2D context (not sub-context) handles of IFC file.</returns>
      private HashSet<IFCAnyHandle> CreateContextInformation(ExporterIFC exporterIFC, Document doc, out IList<double> directionRatios)
      {
         HashSet<IFCAnyHandle> repContexts = new HashSet<IFCAnyHandle>();

         // Make sure this precision value is in an acceptable range.
         double initialPrecision = doc.Application.VertexTolerance / 10.0;
         initialPrecision = Math.Min(initialPrecision, 1e-3);
         initialPrecision = Math.Max(initialPrecision, 1e-8);

         double scaledPrecision = UnitUtil.ScaleLength(initialPrecision);
         int exponent = Convert.ToInt32(Math.Log10(scaledPrecision));
         double precision = Math.Pow(10.0, exponent);

         ExporterCacheManager.LengthPrecision = UnitUtil.UnscaleLength(precision);

         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle wcs = null;
         XYZ unscaledOrigin = XYZ.Zero;

         if (ExporterCacheManager.ExportOptionsCache.ExportingSeparateLink())
         {
            if (CoordReferenceInfo.MainModelGeoRefOrWCS != null)
            {
               unscaledOrigin = CoordReferenceInfo.MainModelGeoRefOrWCS.Origin;
               directionRatios = new List<double>(2) { CoordReferenceInfo.MainModelGeoRefOrWCS.BasisY.X, CoordReferenceInfo.MainModelGeoRefOrWCS.BasisY.Y };
            }
            else
               directionRatios = new List<double>(2) { 0.0, 1.0 };

            if (CoordReferenceInfo.CrsInfo.CrsInfoNotSet || ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            {
               // For IFC2x3 and before, or when IfcMapConversion info is not provided, the eastings, northings, orthogonalHeight will be assigned to the wcs
               XYZ orig = UnitUtil.ScaleLength(unscaledOrigin);
               wcs = ExporterUtil.CreateAxis2Placement3D(file, orig, null, null);
            }
            else
            {
               // In IFC4 onward and EPSG has value (IfcMapConversion will be created), the wcs will be (0,0,0)
               wcs = IFCInstanceExporter.CreateAxis2Placement3D(file, ExporterCacheManager.Global3DOriginHandle, null, null);
            }
         }
         else
         {
            // These full computation for the WCS or map conversion information will be used only for the main model
            // The link models will follow the main model

            SiteTransformBasis transformBasis = ExporterCacheManager.ExportOptionsCache.SiteTransformation;
            ProjectLocation projLocation = ExporterCacheManager.SelectedSiteProjectLocation;

            (double eastings, double northings, double orthogonalHeight, double angleTN, double origAngleTN) geoRefInfo =
               OptionsUtil.GeoReferenceInformation(doc, transformBasis, projLocation);

            CoordReferenceInfo.MainModelTNAngle = geoRefInfo.origAngleTN;
            double trueNorthAngleConverted = geoRefInfo.angleTN + Math.PI / 2.0;
            directionRatios = new List<Double>(2) { Math.Cos(trueNorthAngleConverted), Math.Sin(trueNorthAngleConverted) };

            unscaledOrigin = new XYZ(geoRefInfo.eastings, geoRefInfo.northings, geoRefInfo.orthogonalHeight);
            if (string.IsNullOrEmpty(ExporterCacheManager.ExportOptionsCache.GeoRefEPSGCode) ||
               ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            {
               // For IFC2x3 and before, or when IfcMapConversion info is not provided, the eastings, northings, orthogonalHeight will be assigned to the wcs
               XYZ orig = UnitUtil.ScaleLength(unscaledOrigin);
               wcs = ExporterUtil.CreateAxis2Placement3D(file, orig, null, null);
            }
            else
            {
               // In IFC4 onward and EPSG has value (IfcMapConversion will be created), the wcs will be (0,0,0)
               wcs = IFCInstanceExporter.CreateAxis2Placement3D(file, ExporterCacheManager.Global3DOriginHandle, null, null);
            }
         }

         // This covers Internal case, and Shared case for IFC4+.  
         // NOTE: If new cases appear, they should be covered above.
         if (wcs == null)
         {
            wcs = ExporterUtil.CreateAxis2Placement3D(file, XYZ.Zero, null, null);
         }

         IFCAnyHandle trueNorth = IFCInstanceExporter.CreateDirection(file, directionRatios);
         int dimCount = 3;
         IFCAnyHandle context3D = IFCInstanceExporter.CreateGeometricRepresentationContext(file, null,
             "Model", dimCount, precision, wcs, trueNorth);
         
         ExporterCacheManager.Set3DContextHandle(exporterIFC, IFCRepresentationIdentifier.None, context3D);

         // TODO: Fix regression tests whose line numbers change as a result of missing handles.
         if (!ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly)
         {
            ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Axis);
         }

         // We will force creation of the Body subcontext because internal code expects it.
         // For the rest, we will create sub-contexts as we need them.
         ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Body);

         // TODO: Fix regression tests whose line numbers change as a result of missing handles.
         if (!ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly)
         {
            ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.Box);
            ExporterCacheManager.Get3DContextHandle(IFCRepresentationIdentifier.FootPrint);
         }

         repContexts.Add(context3D); // Only Contexts in list, not sub-contexts.

         // Create IFCMapConversion information for the context
         if (!ExportIFCMapConversion(exporterIFC, doc, context3D, directionRatios))
         {
            // Keep the Transform information for the WCS of the main model to be used later for exporting Link file(s)
            Transform wcsTr = Transform.Identity;
            wcsTr.Origin = unscaledOrigin;
            wcsTr.BasisY = new XYZ(directionRatios[0], directionRatios[1], 0.0);
            wcsTr.BasisZ = new XYZ(0.0, 0.0, 1.0);
            wcsTr.BasisX = wcsTr.BasisY.CrossProduct(wcsTr.BasisZ);
            CoordReferenceInfo.MainModelGeoRefOrWCS = wcsTr;
         }

         if (ExporterCacheManager.ExportOptionsCache.ExportAnnotations)
         {
            IFCAnyHandle context2DHandle = IFCInstanceExporter.CreateGeometricRepresentationContext(file,
                null, "Plan", dimCount, precision, wcs, trueNorth);

            IFCAnyHandle context2D = IFCInstanceExporter.CreateGeometricRepresentationSubContext(file,
                "Annotation", "Plan", context2DHandle, 0.01, IFCGeometricProjection.Plan_View, null);

            ExporterCacheManager.Set2DContextHandle(IFCRepresentationIdentifier.Annotation, context2D);
            ExporterCacheManager.Set2DContextHandle(IFCRepresentationIdentifier.None, context2D);
            
            repContexts.Add(context2DHandle); // Only Contexts in list, not sub-contexts.
         }

         return repContexts;
      }

      private void GetCOBieContactInfo(IFCFile file, Document doc)
      {
         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3ExtendedFMHandoverView)
         {
            try
            {
               string CObieContactXML = Path.GetDirectoryName(doc.PathName) + @"\" + Path.GetFileNameWithoutExtension(doc.PathName) + @"_COBieContact.xml";
               string category = null, company = null, department = null, organizationCode = null, contactFirstName = null, contactFamilyName = null,
                   postalBox = null, town = null, stateRegion = null, postalCode = null, country = null;

               using (XmlReader reader = XmlReader.Create(CObieContactXML))
               {
                  IList<string> eMailAddressList = new List<string>();
                  IList<string> telNoList = new List<string>();
                  IList<string> addressLines = new List<string>();

                  while (reader.Read())
                  {
                     if (reader.IsStartElement())
                     {
                        while (reader.Read())
                        {
                           switch (reader.Name)
                           {
                              case "Email":
                                 eMailAddressList.Add(reader.ReadString());
                                 break;
                              case "Classification":
                                 category = reader.ReadString();
                                 break;
                              case "Company":
                                 company = reader.ReadString();
                                 break;
                              case "Phone":
                                 telNoList.Add(reader.ReadString());
                                 break;
                              case "Department":
                                 department = reader.ReadString();
                                 break;
                              case "OrganizationCode":
                                 organizationCode = reader.ReadString();
                                 break;
                              case "FirstName":
                                 contactFirstName = reader.ReadString();
                                 break;
                              case "LastName":
                                 contactFamilyName = reader.ReadString();
                                 break;
                              case "Street":
                                 addressLines.Add(reader.ReadString());
                                 break;
                              case "POBox":
                                 postalBox = reader.ReadString();
                                 break;
                              case "Town":
                                 town = reader.ReadString();
                                 break;
                              case "State":
                                 stateRegion = reader.ReadString();
                                 break;
                              case "Zip":
                                 category = reader.ReadString();
                                 break;
                              case "Country":
                                 country = reader.ReadString();
                                 break;
                              case "Contact":
                                 if (reader.IsStartElement()) break;     // Do nothing at the start tag, process when it is the end
                                 CreateContact(file, category, company, department, organizationCode, contactFirstName,
                                     contactFamilyName, postalBox, town, stateRegion, postalCode, country,
                                     eMailAddressList, telNoList, addressLines);
                                 // reset variables
                                 {
                                    category = null;
                                    company = null;
                                    department = null;
                                    organizationCode = null;
                                    contactFirstName = null;
                                    contactFamilyName = null;
                                    postalBox = null;
                                    town = null;
                                    stateRegion = null;
                                    postalCode = null;
                                    country = null;
                                    eMailAddressList.Clear();
                                    telNoList.Clear();
                                    addressLines.Clear();
                                 }
                                 break;
                              default:
                                 break;
                           }
                        }
                     }
                  }
               }
            }
            catch
            {
               // Can't find the XML file, ignore the whole process and continue
            }
         }
      }

      private void CreateActor(string actorName, IFCAnyHandle clientOrg, IFCAnyHandle projectHandle,
         HashSet<IFCAnyHandle> projectHandles, IFCAnyHandle ownerHistory, IFCFile file)
      {
         string actorGuid = GUIDUtil.GenerateIFCGuidFrom(GUIDUtil.CreateGUIDString(IFCEntityType.IfcActor, 
            actorName, projectHandle));
         IFCAnyHandle actor = IFCInstanceExporter.CreateActor(file, actorGuid, ownerHistory, 
            null, null, null, clientOrg);

         string actorRelAssignsGuid = GUIDUtil.GenerateIFCGuidFrom(
            GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToActor, actorName, projectHandle));
         IFCInstanceExporter.CreateRelAssignsToActor(file, actorRelAssignsGuid, ownerHistory, actorName,
            null, projectHandles, null, actor, null);
      }

      /// <summary>
      /// Creates the IfcProject.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="doc">The document provides the owner information.</param>
      /// <param name="application">The handle of IFC file to create the owner history.</param>
      private void CreateProject(ExporterIFC exporterIFC, Document doc, IFCAnyHandle application)
      {
         string familyName;
         string givenName;
         List<string> middleNames;
         List<string> prefixTitles;
         List<string> suffixTitles;

         string author = string.Empty;
         bool hasPotentialLastUser = false;

         ProjectInfo projectInfo = !ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly ? doc.ProjectInformation : null;
         if (projectInfo != null)
         {
            try
            {
               author = projectInfo.Author;
               hasPotentialLastUser = !string.IsNullOrWhiteSpace(author);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
               //if failed to get author from project info, try to get the username from application later.
            }
         }

         if (string.IsNullOrEmpty(author))
         {
            author = doc.Application.Username;
         }

         NamingUtil.ParseName(author, out familyName, out givenName, out middleNames, out prefixTitles, out suffixTitles);

         IFCFile file = exporterIFC.GetFile();
         int creationDate = (int)GetCreationDate(doc);
         IFCAnyHandle ownerHistory = null;
         IFCAnyHandle person = null;
         IFCAnyHandle organization = null;
         IFCAnyHandle owningUser = null;

         COBieCompanyInfo cobieCompInfo = ExporterCacheManager.ExportOptionsCache.COBieCompanyInfo;

         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3COBIE24DesignDeliverable && cobieCompInfo != null)
         {
            IFCAnyHandle postalAddress = IFCInstanceExporter.CreatePostalAddress(file, null, null, null, null, new List<string>() { cobieCompInfo.StreetAddress },
               null, cobieCompInfo.City, cobieCompInfo.State_Region, cobieCompInfo.PostalCode, cobieCompInfo.Country);
            IFCAnyHandle telecomAddress = IFCInstanceExporter.CreateTelecomAddress(file, null, null, null, new List<string>() { cobieCompInfo.CompanyPhone },
               null, null, new List<string>() { cobieCompInfo.CompanyEmail }, null);

            organization = IFCInstanceExporter.CreateOrganization(file, null, cobieCompInfo.CompanyName, null,
                null, new List<IFCAnyHandle>() { postalAddress, telecomAddress });
            person = IFCInstanceExporter.CreatePerson(file, null, null, null, null, null, null, null, null);
         }
         else if (!ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly)
         {
            List<IFCAnyHandle> telecomAddresses = null;
            IFCAnyHandle telecomAddress = GetTelecomAddressFromExtStorage(file);
            if (telecomAddress != null)
            {
               telecomAddresses = [ telecomAddress ];
            }

            person = IFCInstanceExporter.CreatePerson(file, null, familyName, givenName, middleNames,
                prefixTitles, suffixTitles, null, telecomAddresses);

            string organizationName = null;
            string organizationDescription = null;
            if (projectInfo != null)
            {
               try
               {
                  organizationName = projectInfo.OrganizationName;
                  organizationDescription = projectInfo.OrganizationDescription;
               }
               catch (Autodesk.Revit.Exceptions.InvalidOperationException)
               {
               }
            }

            organization = IFCInstanceExporter.CreateOrganization(file, null, organizationName, organizationDescription, null, null);
         }

         if (person != null || organization != null)
         {
            owningUser = IFCInstanceExporter.CreatePersonAndOrganization(file, person, organization, null);
         }

         // TODO: Fix regression tests whose line numbers change as a result of missing handles.
         // Change to if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4 || !ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly)
         {
            IFCAnyHandle lastModifyingUser = hasPotentialLastUser && ExporterCacheManager.ExportOptionsCache.OwnerHistoryLastModified
               ? owningUser : null;

            ownerHistory = IFCInstanceExporter.CreateOwnerHistory(file, owningUser, application, null,
            IFCChangeAction.NoChange, null, lastModifyingUser, null, creationDate);

            exporterIFC.SetOwnerHistoryHandle(ownerHistory);    // For use by native code only.
            ExporterCacheManager.OwnerHistoryHandle = ownerHistory;
         }

         // Create mandatory units
         UnitMappingUtil.GetOrCreateUnitInfo(SpecTypeId.Length);
         UnitMappingUtil.GetOrCreateUnitInfo(SpecTypeId.Area);
         UnitMappingUtil.GetOrCreateUnitInfo(SpecTypeId.Volume);

         // Getting contact information from Revit extensible storage that COBie extension tool creates
         GetCOBieContactInfo(file, doc);

         UnitMappingUtil.CreateCobieUnits();

         IList<double> directionRatios = null;
         HashSet<IFCAnyHandle> repContexts = CreateContextInformation(exporterIFC, doc, out directionRatios);

         string projectName = null;
         string projectLongName = null;
         string projectDescription = null;
         string projectPhase = null;

         if (!ExporterCacheManager.ExportOptionsCache.ExportGeometryOnly)
         {
            COBieProjectInfo cobieProjInfo = ExporterCacheManager.ExportOptionsCache.COBieProjectInfo;
            if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3COBIE24DesignDeliverable && cobieProjInfo != null)
            {
               projectName = cobieProjInfo.ProjectName;
               projectDescription = cobieProjInfo.ProjectDescription;
               projectPhase = cobieProjInfo.ProjectPhase;
            }
            else
            {
               // As per IFC implementer's agreement, we get IfcProject.Name from ProjectInfo.Number and IfcProject.Longname from ProjectInfo.Name 
               projectName = projectInfo?.Number;
               projectLongName = projectInfo?.Name;

               // Get project description if it is set in the Project info
               projectDescription = (projectInfo != null) ? NamingUtil.GetDescriptionOverride(projectInfo, null) : null;

               if (projectInfo != null)
                  if (ParameterUtil.GetStringValueFromElement(projectInfo, "IfcProject.Phase", out projectPhase) == null)
                     ParameterUtil.GetStringValueFromElement(projectInfo, "Project Phase", out projectPhase);
            }
         }

         string projectGUID = GUIDUtil.CreateProjectLevelGUID(doc, GUIDUtil.ProjectLevelGUIDType.Project);
         IFCAnyHandle projectHandle = IFCInstanceExporter.CreateProject(exporterIFC, projectInfo, projectGUID, ownerHistory,
            projectName, projectDescription, projectLongName, projectPhase, repContexts, null);
         ExporterCacheManager.ProjectHandle = projectHandle;

         if (projectInfo != null)
         {
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               productWrapper.AddProject(projectInfo, projectHandle);
               ExporterUtil.ExportRelatedProperties(exporterIFC, projectInfo, productWrapper);
            }
         }

         if (ExporterCacheManager.ExportOptionsCache.ExportAsCOBIE)
         {
            HashSet<IFCAnyHandle> projectHandles = new HashSet<IFCAnyHandle>() { projectHandle };
            
            string clientName = (projectInfo != null) ? projectInfo.ClientName : string.Empty;
            IFCAnyHandle clientOrg = IFCInstanceExporter.CreateOrganization(file, null, clientName, null, null, null);

            CreateActor("Project Client/Owner", clientOrg, projectHandle, projectHandles, ownerHistory,
               file);
            CreateActor("Project Architect", clientOrg, projectHandle, projectHandles, ownerHistory,
               file);
         }
      }

      private void CreateContact(IFCFile file, string category, string company, string department, string organizationCode, string contactFirstName,
          string contactFamilyName, string postalBox, string town, string stateRegion, string postalCode, string country,
          IList<string> eMailAddressList, IList<string> telNoList, IList<string> addressLines)
      {
         IFCAnyHandle contactTelecomAddress = IFCInstanceExporter.CreateTelecomAddress(file, null, null, null, telNoList, null, null, eMailAddressList, null);
         IFCAnyHandle contactPostalAddress = IFCInstanceExporter.CreatePostalAddress(file, null, null, null, department, addressLines, postalBox, town, stateRegion,
                 postalCode, country);
         List<IFCAnyHandle> contactAddresses = [ contactTelecomAddress, contactPostalAddress ];
         IFCAnyHandle contactPerson = IFCInstanceExporter.CreatePerson(file, null, contactFamilyName, contactFirstName, null,
             null, null, null, contactAddresses);
         IFCAnyHandle contactOrganization = IFCInstanceExporter.CreateOrganization(file, organizationCode, company, null,
             null, null);
         IFCAnyHandle actorRole = IFCInstanceExporter.CreateActorRole(file, "UserDefined", category, null);
         List<IFCAnyHandle> actorRoles = [actorRole ];
         IFCAnyHandle contactEntry = IFCInstanceExporter.CreatePersonAndOrganization(file, contactPerson, contactOrganization, actorRoles);
      }

      private IFCAnyHandle GetTelecomAddressFromExtStorage(IFCFile file)
      {
         IFCFileHeaderItem fHItem = ExporterCacheManager.ExportOptionsCache.FileHeaderItem;
         if (string.IsNullOrEmpty(fHItem.AuthorEmail))
         {
            return null;
         }

         List<string> electronicMailAddress = [fHItem.AuthorEmail];
         return IFCInstanceExporter.CreateTelecomAddress(file, null, null, null, null, null, null, electronicMailAddress, null);
      }

      /// <summary>
      /// Create IFC Address from the saved data obtained by the UI and saved in the extensible storage
      /// </summary>
      /// <param name="file"></param>
      /// <param name="document"></param>
      /// <returns>The handle of IFC file.</returns>
      static public IFCAnyHandle CreateIFCAddressFromExtStorage(IFCFile file, Document document)
      {
         IFCAddress savedAddress = new();
         IFCAddressItem savedAddressItem;

         if (savedAddress.GetSavedAddress(document, out savedAddressItem) == true)
         {
            if (!savedAddressItem.HasData())
               return null;

            IFCAnyHandle postalAddress;

            // We have address saved in the extensible storage
            List<string> addressLines = null;
            if (!string.IsNullOrEmpty(savedAddressItem.AddressLine1))
            {
               addressLines = new List<string>();

               addressLines.Add(savedAddressItem.AddressLine1);
               if (!string.IsNullOrEmpty(savedAddressItem.AddressLine2))
                  addressLines.Add(savedAddressItem.AddressLine2);
            }

            IFCAddressType? addressPurpose = null;
            if (!string.IsNullOrEmpty(savedAddressItem.Purpose))
            {
               addressPurpose = IFCAddressType.UserDefined;     // set this as default value
               if (string.Compare(savedAddressItem.Purpose, "OFFICE", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.Office;
               else if (string.Compare(savedAddressItem.Purpose, "SITE", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.Site;
               else if (string.Compare(savedAddressItem.Purpose, "HOME", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.Home;
               else if (string.Compare(savedAddressItem.Purpose, "DISTRIBUTIONPOINT", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.DistributionPoint;
               else if (string.Compare(savedAddressItem.Purpose, "USERDEFINED", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.UserDefined;
            }

            postalAddress = IFCInstanceExporter.CreatePostalAddress(file, addressPurpose, savedAddressItem.Description, savedAddressItem.UserDefinedPurpose,
               savedAddressItem.InternalLocation, addressLines, savedAddressItem.POBox, savedAddressItem.TownOrCity, savedAddressItem.RegionOrState, savedAddressItem.PostalCode,
               savedAddressItem.Country);

            return postalAddress;
         }

         return null;
      }

      /// <summary>
      /// Check whether there is address information that is not empty and needs to be created for IfcSite
      /// </summary>
      /// <param name="document">the document</param>
      /// <returns>true if address is to be created for the site</returns>
      static public bool NeedToCreateAddressForSite(Document document)
      {
         IFCAddress savedAddress = new IFCAddress();
         IFCAddressItem savedAddressItem;
         if (savedAddress.GetSavedAddress(document, out savedAddressItem) == true)
         {
            // Return the selection checkbox regardless whether it has data. It will be checked before the creaton of the postal address later
            return savedAddressItem.AssignAddressToSite;
         }
         return false;  //default not creating site address if not set in the ui
      }

      /// <summary>
      /// Check whether there is address information that is not empty and needs to be created for IfcBuilding
      /// </summary>
      /// <param name="document">the document</param>
      /// <returns>true if address is to be created for the building</returns>
      static public bool NeedToCreateAddressForBuilding(Document document)
      {
         IFCAddress savedAddress = new IFCAddress();
         IFCAddressItem savedAddressItem;
         if (savedAddress.GetSavedAddress(document, out savedAddressItem) == true)
         {
            // Return the selection checkbox regardless whether it has data. It will be checked before the creaton of the postal address later
            return savedAddressItem.AssignAddressToBuilding;
         }
         return true;   //default when there is no address information from the export UI, so that it will try to get other information from project info or location
      }

      /// <summary>
      /// Creates the IfcPostalAddress, and assigns it to the file.
      /// </summary>
      /// <param name="file">The IFC file.</param>
      /// <param name="address">The address string.</param>
      /// <param name="town">The town string.</param>
      /// <returns>The handle of IFC file.</returns>
      static public IFCAnyHandle CreateIFCAddress(IFCFile file, Document document, ProjectInfo projInfo)
      {
         IFCAnyHandle postalAddress = null;
         postalAddress = CreateIFCAddressFromExtStorage(file, document);
         if (postalAddress != null)
            return postalAddress;

         string projectAddress = projInfo != null ? projInfo.Address : string.Empty;
         SiteLocation siteLoc = ExporterCacheManager.SelectedSiteProjectLocation?.GetSiteLocation();
         string location = siteLoc != null ? siteLoc.PlaceName : string.Empty;

         if (projectAddress == null)
            projectAddress = string.Empty;
         if (location == null)
            location = string.Empty;

         List<string> parsedAddress = new List<string>();
         string city = string.Empty;
         string state = string.Empty;
         string postCode = string.Empty;
         string country = string.Empty;

         string parsedTown = location;
         int commaLoc = -1;
         do
         {
            commaLoc = parsedTown.IndexOf(',');
            if (commaLoc >= 0)
            {
               if (commaLoc > 0)
                  parsedAddress.Add(parsedTown.Substring(0, commaLoc));
               parsedTown = parsedTown.Substring(commaLoc + 1).TrimStart(' ');
            }
            else if (!string.IsNullOrEmpty(parsedTown))
               parsedAddress.Add(parsedTown);
         } while (commaLoc >= 0);

         int numLines = parsedAddress.Count;
         if (numLines > 0)
         {
            country = parsedAddress[numLines - 1];
            numLines--;
         }

         if (numLines > 0)
         {
            int spaceLoc = parsedAddress[numLines - 1].IndexOf(' ');
            if (spaceLoc > 0)
            {
               state = parsedAddress[numLines - 1].Substring(0, spaceLoc);
               postCode = parsedAddress[numLines - 1].Substring(spaceLoc + 1);
            }
            else
               state = parsedAddress[numLines - 1];
            numLines--;
         }

         if (numLines > 0)
         {
            city = parsedAddress[numLines - 1];
            numLines--;
         }

         List<string> addressLines = new List<string>();
         if (!string.IsNullOrEmpty(projectAddress))
            addressLines.Add(projectAddress);

         for (int ii = 0; ii < numLines; ii++)
         {
            addressLines.Add(parsedAddress[ii]);
         }

         postalAddress = IFCInstanceExporter.CreatePostalAddress(file, null, null, null,
            null, addressLines, null, city, state, postCode, country);

         return postalAddress;
      }
      
      /// <summary>
      /// Creates the global direction and sets the cardinal directions in 3D.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      private void CreateGlobalDirection(ExporterIFC exporterIFC)
      {
         // Note that we do not use the ExporterUtil.CreateDirection functions below, as they try
         // to match the input XYZ to one of the "global" directions that we are creating below.
         IFCAnyHandle xDirPos = null;
         IFCAnyHandle xDirNeg = null;
         IFCAnyHandle yDirPos = null;
         IFCAnyHandle yDirNeg = null;
         IFCAnyHandle zDirPos = null;
         IFCAnyHandle zDirNeg = null;

         IFCFile file = exporterIFC.GetFile();
         IList<double> xxp = new List<double>();
         xxp.Add(1.0); xxp.Add(0.0); xxp.Add(0.0);
         xDirPos = IFCInstanceExporter.CreateDirection(file, xxp);

         IList<double> xxn = new List<double>();
         xxn.Add(-1.0); xxn.Add(0.0); xxn.Add(0.0);
         xDirNeg = IFCInstanceExporter.CreateDirection(file, xxn);

         IList<double> yyp = new List<double>();
         yyp.Add(0.0); yyp.Add(1.0); yyp.Add(0.0);
         yDirPos = IFCInstanceExporter.CreateDirection(file, yyp);

         IList<double> yyn = new List<double>();
         yyn.Add(0.0); yyn.Add(-1.0); yyn.Add(0.0);
         yDirNeg = IFCInstanceExporter.CreateDirection(file, yyn);

         IList<double> zzp = new List<double>();
         zzp.Add(0.0); zzp.Add(0.0); zzp.Add(1.0);
         zDirPos = IFCInstanceExporter.CreateDirection(file, zzp);

         IList<double> zzn = new List<double>();
         zzn.Add(0.0); zzn.Add(0.0); zzn.Add(-1.0);
         zDirNeg = IFCInstanceExporter.CreateDirection(file, zzn);

         ExporterIFCUtils.SetGlobal3DDirectionHandles(true, xDirPos, yDirPos, zDirPos);
         ExporterIFCUtils.SetGlobal3DDirectionHandles(false, xDirNeg, yDirNeg, zDirNeg);
      }

      /// <summary>
      /// Creates the global direction and sets the cardinal directions in 2D.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      private void CreateGlobalDirection2D(ExporterIFC exporterIFC)
      {
         IFCAnyHandle xDirPos2D = null;
         IFCAnyHandle xDirNeg2D = null;
         IFCAnyHandle yDirPos2D = null;
         IFCAnyHandle yDirNeg2D = null;
         IFCFile file = exporterIFC.GetFile();

         IList<double> xxp = new List<double>();
         xxp.Add(1.0); xxp.Add(0.0);
         xDirPos2D = IFCInstanceExporter.CreateDirection(file, xxp);

         IList<double> xxn = new List<double>();
         xxn.Add(-1.0); xxn.Add(0.0);
         xDirNeg2D = IFCInstanceExporter.CreateDirection(file, xxn);

         IList<double> yyp = new List<double>();
         yyp.Add(0.0); yyp.Add(1.0);
         yDirPos2D = IFCInstanceExporter.CreateDirection(file, yyp);

         IList<double> yyn = new List<double>();
         yyn.Add(0.0); yyn.Add(-1.0);
         yDirNeg2D = IFCInstanceExporter.CreateDirection(file, yyn);
         ExporterIFCUtils.SetGlobal2DDirectionHandles(true, xDirPos2D, yDirPos2D);
         ExporterIFCUtils.SetGlobal2DDirectionHandles(false, xDirNeg2D, yDirNeg2D);
      }

      /// <summary>
      /// Creates the global cartesian origin then sets the 3D and 2D origins.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      private void CreateGlobalCartesianOrigin(ExporterIFC exporterIFC)
      {

         IFCAnyHandle origin2d = null;
         IFCAnyHandle origin = null;

         IFCFile file = exporterIFC.GetFile();
         IList<double> measure = new List<double>();
         measure.Add(0.0); measure.Add(0.0); measure.Add(0.0);
         origin = IFCInstanceExporter.CreateCartesianPoint(file, measure);

         IList<double> measure2d = new List<double>();
         measure2d.Add(0.0); measure2d.Add(0.0);
         origin2d = IFCInstanceExporter.CreateCartesianPoint(file, measure2d);
         ExporterIFCUtils.SetGlobal3DOriginHandle(origin);
         ExporterIFCUtils.SetGlobal2DOriginHandle(origin2d);
      }

      private static bool ValidateContainedHandle(IFCAnyHandle initialHandle)
      {
         if (ExporterCacheManager.ElementsInAssembliesCache.Contains(initialHandle))
            return false;

         try
         {
            if (!IFCAnyHandleUtil.HasRelDecomposes(initialHandle))
               return true;
         }
         catch
         {
         }

         return false;
      }

      /// <summary>
      /// Remove contained or invalid handles from this set.
      /// </summary>
      /// <param name="initialSet">The initial set that may have contained or invalid handles.</param>
      /// <returns>A cleaned set.</returns>
      public static HashSet<IFCAnyHandle> RemoveContainedHandlesFromSet(ICollection<IFCAnyHandle> initialSet)
      {
         HashSet<IFCAnyHandle> filteredSet = new HashSet<IFCAnyHandle>();

         if (initialSet != null)
         {
            foreach (IFCAnyHandle initialHandle in initialSet)
            {
               if (ValidateContainedHandle(initialHandle))
                  filteredSet.Add(initialHandle);
            }
         }

         return filteredSet;
      }

      private class IFCLevelExportInfo
      {
         public IFCLevelExportInfo() { }

         public IDictionary<ElementId, IList<IFCLevelInfo>> LevelMapping { get; set; } =
            new Dictionary<ElementId, IList<IFCLevelInfo>>();

         public IList<IFCLevelInfo> OrphanedLevelInfos { get; set; } = new List<IFCLevelInfo>();

         public void UnionLevelInfoRelated(ElementId toLevelId, IFCLevelInfo fromLevel)
         {
            if (fromLevel == null)
               return;

            if (toLevelId == ElementId.InvalidElementId)
            {
               OrphanedLevelInfos.Add(fromLevel);
               return;
            }

            IList<IFCLevelInfo> levelMappingList;
            if (!LevelMapping.TryGetValue(toLevelId, out levelMappingList))
            {
               levelMappingList = new List<IFCLevelInfo>();
               LevelMapping[toLevelId] = levelMappingList;
            }
            levelMappingList.Add(fromLevel);
         }

         public void TransferOrphanedLevelInfo(ElementId toLevelId)
         {
            if (toLevelId == ElementId.InvalidElementId)
               return;

            if (OrphanedLevelInfos.Count == 0)
               return;

            IList<IFCLevelInfo> toLevelMappingList;
            if (!LevelMapping.TryGetValue(toLevelId, out toLevelMappingList))
            {
               toLevelMappingList = new List<IFCLevelInfo>();
               LevelMapping[toLevelId] = toLevelMappingList;
            }

            foreach (IFCLevelInfo orphanedLevelInfo in OrphanedLevelInfos)
            {
               toLevelMappingList.Add(orphanedLevelInfo);
            }
            OrphanedLevelInfos.Clear();
         }

         public Tuple<HashSet<IFCAnyHandle>, HashSet<IFCAnyHandle>> CollectValidHandlesForLevel(
            ElementId levelId, IFCLevelInfo levelInfo)
         {
            if (levelId == ElementId.InvalidElementId || levelInfo == null)
               return null;

            HashSet<IFCAnyHandle> levelRelatedProducts = new HashSet<IFCAnyHandle>();
            levelRelatedProducts.UnionWith(levelInfo.GetRelatedProducts());

            HashSet<IFCAnyHandle> levelRelatedElements = new HashSet<IFCAnyHandle>();
            levelRelatedElements.UnionWith(levelInfo.GetRelatedElements());

            IList<IFCLevelInfo> levelMappingList;
            if (LevelMapping.TryGetValue(levelId, out levelMappingList))
            {
               foreach (IFCLevelInfo containedLevelInfo in levelMappingList)
               {
                  if (containedLevelInfo != null)
                  {
                     levelRelatedProducts.UnionWith(containedLevelInfo.GetRelatedProducts());
                     levelRelatedElements.UnionWith(containedLevelInfo.GetRelatedElements());
                  }
               }
            }

            return Tuple.Create(RemoveContainedHandlesFromSet(levelRelatedProducts),
               RemoveContainedHandlesFromSet(levelRelatedElements));
         }

         public HashSet<IFCAnyHandle> CollectOrphanedHandles()
         {
            HashSet<IFCAnyHandle> orphanedHandles = new HashSet<IFCAnyHandle>();
            foreach (IFCLevelInfo containedLevelInfo in OrphanedLevelInfos)
            {
               if (containedLevelInfo != null)
               {
                  orphanedHandles.UnionWith(containedLevelInfo.GetRelatedProducts());
                  orphanedHandles.UnionWith(containedLevelInfo.GetRelatedElements());
               }
            }

            // RemoveContainedHandlesFromSet will be called before these are used, so
            // don't bother doing it twice.
            return orphanedHandles;
         }
      }

      private IFCExportElement GetExportLevelState(Element level)
      {
         if (level == null)
            return IFCExportElement.ByType;

         Element levelType = level.Document.GetElement(level.GetTypeId());
         IFCExportElement? exportLevel =
            ElementFilteringUtil.GetExportElementState(level, levelType);

         return exportLevel.GetValueOrDefault(IFCExportElement.ByType);
      }
      
      private bool NeverExportLevel(Element level)
      {
         return GetExportLevelState(level) == IFCExportElement.No;
      }

      private bool AlwaysExportLevel(Element level)
      {
         return GetExportLevelState(level) == IFCExportElement.Yes;
      }

      /// <summary>
      /// Relate levels and products.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="document">The document to relate the levels.</param>
      private void RelateLevels(ExporterIFC exporterIFC, Document document)
      {
         HashSet<IFCAnyHandle> buildingStories = new HashSet<IFCAnyHandle>();
         IList<ElementId> levelIds = ExporterCacheManager.LevelInfoCache.GetLevelsByElevation();
         IFCFile file = exporterIFC.GetFile();

         ElementId lastValidLevelId = ElementId.InvalidElementId;
         IFCLevelExportInfo levelInfoMapping = new IFCLevelExportInfo();

         for (int ii = 0; ii < levelIds.Count; ii++)
         {
            ElementId levelId = levelIds[ii];
            IFCLevelInfo levelInfo = ExporterCacheManager.LevelInfoCache.GetLevelInfo(levelId);
            if (levelInfo == null)
               continue;

            Element level = document.GetElement(levelId);

            levelInfoMapping.TransferOrphanedLevelInfo(levelId);
            int nextLevelIdx = ii + 1;

            // We may get stale handles in here; protect against this.
            Tuple<HashSet<IFCAnyHandle>, HashSet<IFCAnyHandle>> productsAndElements =
               levelInfoMapping.CollectValidHandlesForLevel(levelId, levelInfo);

            HashSet<IFCAnyHandle> relatedProducts = productsAndElements.Item1;
            HashSet<IFCAnyHandle> relatedElements = productsAndElements.Item2;

            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, false))
            {
               IFCAnyHandle buildingStoreyHandle = levelInfo.GetBuildingStorey();
               if (!buildingStories.Contains(buildingStoreyHandle))
               {
                  buildingStories.Add(buildingStoreyHandle);
                  IFCExportInfoPair exportInfo = new IFCExportInfoPair(IFCEntityType.IfcBuildingStorey);

                  // Add Property set, quantities and classification of Building Storey also to IFC
                  productWrapper.AddElement(level, buildingStoreyHandle, levelInfo, null, false, exportInfo);

                  ExporterUtil.ExportRelatedProperties(exporterIFC, level, productWrapper);
               }
            }

            if (relatedProducts.Count > 0)
            {
               IFCAnyHandle buildingStorey = levelInfo.GetBuildingStorey();
               string guid = GUIDUtil.CreateSubElementGUID(level, (int)IFCBuildingStoreySubElements.RelAggregates);
               ExporterCacheManager.ContainmentCache.AddRelations(buildingStorey, guid, relatedProducts);
            }

            if (relatedElements.Count > 0)
            {
               string guid = GUIDUtil.CreateSubElementGUID(level, (int)IFCBuildingStoreySubElements.RelContainedInSpatialStructure);
               IFCInstanceExporter.CreateRelContainedInSpatialStructure(file, guid, ExporterCacheManager.OwnerHistoryHandle, null, null, relatedElements, levelInfo.GetBuildingStorey());
            }

            ii = nextLevelIdx - 1;
         }

         if (buildingStories.Count > 0)
         {
            IFCAnyHandle buildingHnd = ExporterCacheManager.BuildingHandle;
            ProjectInfo projectInfo = document.ProjectInformation;
            string guid = GUIDUtil.CreateSubElementGUID(projectInfo, (int)IFCProjectSubElements.RelAggregatesBuildingStories);
            ExporterCacheManager.ContainmentCache.AddRelations(buildingHnd, guid, buildingStories);
         }

         // We didn't find a level for this.  Put it in the IfcBuilding, IfcSite, or IfcProject later.
         HashSet<IFCAnyHandle> orphanedHandles = levelInfoMapping.CollectOrphanedHandles();

         ExporterCacheManager.LevelInfoCache.OrphanedElements.UnionWith(orphanedHandles);
      }

      /// <summary>
      /// Clear all delegates.
      /// </summary>
      private void DelegateClear()
      {
         m_ElementExporter = null;
         m_QuantitiesToExport = null;
      }

      private IFCAnyHandle CreateBuildingPlacement(IFCFile file)
      {
         return IFCInstanceExporter.CreateLocalPlacement(file, null, ExporterUtil.CreateAxis2Placement3D(file));
      }

      private IFCAnyHandle CreateFacilityFromProjectInfo(ExporterIFC exporterIFC, Document document, 
         IFCAnyHandle facilityPlacement, bool allowBuildingExport)
      {
         KnownFacilityTypes facilityType = ExporterCacheManager.ExportOptionsCache.FacilityType;
         bool exportingBuilding = facilityType == KnownFacilityTypes.Building;
         if (exportingBuilding && !allowBuildingExport)
            return null;

         COBieProjectInfo cobieProjectInfo = ExporterCacheManager.ExportOptionsCache.COBieProjectInfo;
         bool exportingCOBIE = exportingBuilding && 
            ExporterCacheManager.ExportOptionsCache.ExportAs2x3COBIE24DesignDeliverable && 
            cobieProjectInfo != null;

         ProjectInfo projectInfo = document.ProjectInformation;
         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

         string facilityName = string.Empty;
         string facilityDescription = null;
         string facilityLongName = null;
         string facilityObjectType = null;

         if (exportingCOBIE)
         {
            facilityName = cobieProjectInfo.BuildingName_Number;
            facilityDescription = cobieProjectInfo.BuildingDescription;
         }
         else if (projectInfo != null)
         {
            try
            {
               facilityName = projectInfo.BuildingName;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
            }

            facilityDescription = NamingUtil.GetOverrideStringValue(projectInfo, "FacilityDescription", null);
            facilityLongName = NamingUtil.GetOverrideStringValue(projectInfo, "FacilityLongName", facilityName);
            facilityObjectType = NamingUtil.GetOverrideStringValue(projectInfo, "FacilityObjectType", null);
         }

         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle address = exportingBuilding && NeedToCreateAddressForBuilding(document) ?
            CreateIFCAddress(file, document, projectInfo) : null;

         string facilityPredefinedType = ExporterCacheManager.ExportOptionsCache.FacilityPredefinedType;

         string facilityGUID = GUIDUtil.CreateProjectLevelGUID(document, GUIDUtil.ProjectLevelGUIDType.Building);
         IFCAnyHandle facilityHandle = null;

         switch (facilityType)
         {
            case KnownFacilityTypes.Building:
               facilityHandle = IFCInstanceExporter.CreateBuilding(exporterIFC,
                   facilityGUID, ownerHistory, facilityName, facilityDescription, facilityObjectType, facilityPlacement, null,
                   facilityLongName, IFCElementComposition.Element, null, null, address);
               break;
            case KnownFacilityTypes.Bridge:
               facilityHandle =IFCInstanceExporter.CreateBridge(exporterIFC,
                   facilityGUID, ownerHistory, facilityName, facilityDescription, facilityObjectType, facilityPlacement, null,
                   facilityLongName, IFCElementComposition.Element, facilityPredefinedType);
               break;
            case KnownFacilityTypes.MarineFacility:
               facilityHandle = IFCInstanceExporter.CreateMarineFacility(exporterIFC,
                   facilityGUID, ownerHistory, facilityName, facilityDescription, facilityObjectType, facilityPlacement, null,
                   facilityLongName, IFCElementComposition.Element, facilityPredefinedType);
               break;
            case KnownFacilityTypes.Road:
               facilityHandle = IFCInstanceExporter.CreateRoad(exporterIFC,
                   facilityGUID, ownerHistory, facilityName, facilityDescription, facilityObjectType, facilityPlacement, null,
                   facilityLongName, IFCElementComposition.Element, facilityPredefinedType);
               break;
            case KnownFacilityTypes.Railway:
               facilityHandle = IFCInstanceExporter.CreateRailway(exporterIFC,
                   facilityGUID, ownerHistory, facilityName, facilityDescription, facilityObjectType, facilityPlacement, null,
                   facilityLongName, IFCElementComposition.Element, facilityPredefinedType);
               break;
         }

         if (exportingCOBIE)
         {
            string classificationParamValue = cobieProjectInfo.BuildingType;

            if (ClassificationUtil.ParseClassificationCode(classificationParamValue, "dummy",
               out _, out string classificationItemCode, out string classificationItemName) &&
               !string.IsNullOrEmpty(classificationItemCode))
            {
               string relGuidName = classificationItemCode + ":" + classificationItemName;
               string relGuid = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesClassification, relGuidName,
                  facilityHandle));
               ClassificationReferenceKey key = new ClassificationReferenceKey(null,
                  classificationItemCode, classificationItemName, null, null);
               ExporterCacheManager.ClassificationCache.AddRelation(file, key, relGuid,
                  "BuildingType", facilityHandle);
            }
         }

         ExporterCacheManager.BuildingHandle = facilityHandle;
         return facilityHandle;
      }

      /// <summary>
      /// Create IFCMapConversion that is from IFC4 onward capturing information for geo referencing
      /// </summary>
      /// <param name="exporterIFC">ExporterIFC</param>
      /// <param name="doc">the Document</param>
      /// <param name="geomRepContext">The GeometricRepresentationContex</param>
      /// <param name="TNDirRatio">TrueNorth direction ratios</param>
      private bool ExportIFCMapConversion(ExporterIFC exporterIFC, Document doc, IFCAnyHandle geomRepContext, IList<double> TNDirRatio)
      {
         // Get information from Project info Parameters for Project Global Position and Coordinate Reference System
         if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            return false;

         ProjectInfo projectInfo = doc.ProjectInformation;

         IFCFile file = exporterIFC.GetFile();

         // Explanation:
         // The Survey Point will carry the Northings and Eastings of the known Survey Point usually near to the project. 
         //    This is relative to the reference (0,0) of the Map Projection system used (EPSG: xxxx)
         //    This usually can be accomplished by first locating the Survey Point to the geo reference (0,0) (usually -x, -y of the known coordinate of the Survey point)
         //       It is then moved back UNCLIPPED to the original location
         //    This essentially create the shared location at the map reference (0,0)

         string epsgCode = null;
         string crsDescription = null;
         string crsGeodeticDatum = null;
         IFCAnyHandle crsMapUnit = null;
         string crsMapUnitStr = null;

         double? scale = null;
         string crsVerticalDatum = null;
         string crsMapProjection = null;
         string crsMapZone = null;

         double eastings = 0.0;
         double northings = 0.0;
         double orthogonalHeight = 0.0;
         double? xAxisAbscissa = null;
         double? xAxisOrdinate = null;

         if (ExporterCacheManager.ExportOptionsCache.ExportingSeparateLink())
         {
            if (CoordReferenceInfo.CrsInfo.CrsInfoNotSet)
               return false;

            crsDescription = CoordReferenceInfo.CrsInfo.GeoRefCRSDesc;
            crsGeodeticDatum = CoordReferenceInfo.CrsInfo.GeoRefGeodeticDatum;
            epsgCode = CoordReferenceInfo.CrsInfo.GeoRefCRSName;
            crsMapUnitStr = CoordReferenceInfo.CrsInfo.GeoRefMapUnit;
            crsVerticalDatum = CoordReferenceInfo.CrsInfo.GeoRefVerticalDatum;
            crsMapProjection = CoordReferenceInfo.CrsInfo.GeoRefMapProjection;
            crsMapZone = CoordReferenceInfo.CrsInfo.GeoRefMapZone;

            eastings = CoordReferenceInfo.MainModelGeoRefOrWCS.Origin.X;
            northings = CoordReferenceInfo.MainModelGeoRefOrWCS.Origin.Y;
            orthogonalHeight = CoordReferenceInfo.MainModelGeoRefOrWCS.Origin.Z;

            xAxisAbscissa = CoordReferenceInfo.MainModelGeoRefOrWCS.BasisX.X;
            xAxisOrdinate = CoordReferenceInfo.MainModelGeoRefOrWCS.BasisX.Y;
         }
         else
         {
            if (ParameterUtil.GetStringValueFromElement(projectInfo, "IfcProjectedCRS.VerticalDatum", out crsVerticalDatum) == null)
               ParameterUtil.GetStringValueFromElement(projectInfo, "ProjectGlobalPositioning.CRSVerticalDatum", out crsVerticalDatum);

            if (ParameterUtil.GetStringValueFromElement(projectInfo, "IfcProjectedCRS.MapProjection", out crsMapProjection) == null)
               ParameterUtil.GetStringValueFromElement(projectInfo, "ProjectGlobalPositioning.CRSMapProjection", out crsMapProjection);

            if (ParameterUtil.GetStringValueFromElement(projectInfo, "IfcProjectedCRS.MapZone", out crsMapZone) == null)
               ParameterUtil.GetStringValueFromElement(projectInfo, "ProjectGlobalPositioning.CRSMapZone", out crsMapZone);

            //string defaultEPSGCode = "EPSG:3857";     // Default to EPSG:3857, which is the commonly used ProjectedCR as in GoogleMap, OpenStreetMap
            crsMapUnitStr = ExporterCacheManager.ExportOptionsCache.GeoRefMapUnit;
            (string projectedCRSName, string projectedCRSDesc, string epsgCode, string geodeticDatum, string uom) crsInfo = (null, null, null, null, null);
            if (string.IsNullOrEmpty(ExporterCacheManager.ExportOptionsCache.GeoRefEPSGCode))
            {
               // Only CRSName is mandatory. Paramater sets in the ProjectInfo will override any value if any
               if (string.IsNullOrEmpty(epsgCode))
               {
                  // Try to get the GIS Coordinate System id from SiteLocation
                  crsInfo = OptionsUtil.GetEPSGCodeFromGeoCoordDef(doc.SiteLocation);
                  if (!string.IsNullOrEmpty(crsInfo.projectedCRSName) && !string.IsNullOrEmpty(crsInfo.epsgCode))
                     epsgCode = crsInfo.epsgCode;

                  crsMapUnitStr = crsInfo.uom;
               }
            }
            else
            {
               epsgCode = ExporterCacheManager.ExportOptionsCache.GeoRefEPSGCode;
            }

            // No need to create IfcMapConversion and IfcProjectedCRS if the georeference information is not provided
            if (string.IsNullOrEmpty(epsgCode))
            {
               // Clear any information in the CrsInfo if nothing to set for MapConversion
               CoordReferenceInfo.CrsInfo.Clear();
               return false;
            }

            // IFC only "accepts" EPSG. see https://standards.buildingsmart.org/MVD/RELEASE/IFC4/ADD2_TC1/RV1_2/HTML/schema/ifcrepresentationresource/lexical/ifccoordinatereferencesystem.htm
            if (!epsgCode.StartsWith("EPSG:", StringComparison.InvariantCultureIgnoreCase))
            {
               // The value may contain only number, which means it it EPSG:<the number>
               int epsgNum = -1;
               if (int.TryParse(epsgCode, out epsgNum))
               {
                  epsgCode = "EPSG:" + epsgCode;
               }
            }

            SiteTransformBasis wcsBasis = ExporterCacheManager.ExportOptionsCache.SiteTransformation;
            (double eastings, double northings, double orthogonalHeight, double angleTN, double origAngleTN) geoRefInfo =
                  OptionsUtil.GeoReferenceInformation(doc, wcsBasis, ExporterCacheManager.SelectedSiteProjectLocation);
            eastings = geoRefInfo.eastings;
            northings = geoRefInfo.northings;
            orthogonalHeight = geoRefInfo.orthogonalHeight;

            if (TNDirRatio != null)
            {
               xAxisAbscissa = TNDirRatio.Count > 0 ? TNDirRatio[1] : 0;
               xAxisOrdinate = TNDirRatio.Count > 1 ? TNDirRatio[0] : 0;
            }

            crsDescription = ExporterCacheManager.ExportOptionsCache.GeoRefCRSDesc;
            if (string.IsNullOrEmpty(crsDescription) && !string.IsNullOrEmpty(crsInfo.projectedCRSDesc))
               crsDescription = crsInfo.projectedCRSDesc;
            crsGeodeticDatum = ExporterCacheManager.ExportOptionsCache.GeoRefGeodeticDatum;
            if (string.IsNullOrEmpty(crsGeodeticDatum) && !string.IsNullOrEmpty(crsInfo.geodeticDatum))
               crsGeodeticDatum = crsInfo.geodeticDatum;
         }

         // Handle map unit
         ForgeTypeId utId = UnitTypeId.Meters;
         if (!string.IsNullOrEmpty(crsMapUnitStr))
         {
            if (crsMapUnitStr.EndsWith("Metre", StringComparison.InvariantCultureIgnoreCase) || crsMapUnitStr.EndsWith("Meter", StringComparison.InvariantCultureIgnoreCase))
            {
               IFCSIPrefix? prefix = null;
               if (crsMapUnitStr.Length > 5)
               {
                  string prefixStr = crsMapUnitStr.Substring(0, crsMapUnitStr.Length - 5);
                  if (Enum.TryParse(prefixStr, true, out IFCSIPrefix prefixEnum))
                  {
                     prefix = prefixEnum;
                     switch (prefix)
                     {
                        // Handle SI Units from MM to M. Somehow UnitTypeId does not have larger than M units (It is unlikely to have it in the EPSG anyway)
                        case IFCSIPrefix.Deci:
                           utId = UnitTypeId.Decimeters;
                           break;
                        case IFCSIPrefix.Centi:
                           utId = UnitTypeId.Centimeters;
                           break;
                        case IFCSIPrefix.Milli:
                           utId = UnitTypeId.Millimeters;
                           break;
                        default:
                           utId = UnitTypeId.Meters;
                           break;
                     }
                  }
               }
               crsMapUnit = IFCInstanceExporter.CreateSIUnit(file, IFCUnit.LengthUnit, prefix, IFCSIUnitName.Metre);
            }
            else
            {
               double lengthScaleFactor = 1.0;
               if (crsMapUnitStr.Equals("inch", StringComparison.InvariantCultureIgnoreCase))
               {
                  lengthScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Inches);
                  utId = UnitTypeId.Inches;
               }
               else if (crsMapUnitStr.Equals("foot", StringComparison.InvariantCultureIgnoreCase))
               {
                  lengthScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Feet);
                  utId = UnitTypeId.Feet;
               }

               double lengthSIScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Meters) / lengthScaleFactor;
               IFCAnyHandle lenDims = IFCInstanceExporter.CreateDimensionalExponents(file, 1, 0, 0, 0, 0, 0, 0); // length
               IFCAnyHandle lenSIUnit = IFCInstanceExporter.CreateSIUnit(file, IFCUnit.LengthUnit, null, IFCSIUnitName.Metre);
               IFCAnyHandle lenConvFactor = IFCInstanceExporter.CreateMeasureWithUnit(file, Toolkit.IFCDataUtil.CreateAsLengthMeasure(lengthSIScaleFactor),
                   lenSIUnit);

               crsMapUnit = IFCInstanceExporter.CreateConversionBasedUnit(file, lenDims, IFCUnit.LengthUnit, crsMapUnitStr, lenConvFactor);
            }
         }

         IFCAnyHandle projectedCRS = null;
         projectedCRS = IFCInstanceExporter.CreateProjectedCRS(file, epsgCode, crsDescription, crsGeodeticDatum, crsVerticalDatum,
            crsMapProjection, crsMapZone, crsMapUnit);

         // Only eastings, northings, and orthogonalHeight are mandatory beside the CRSSource (GeometricRepresentationContext) and CRSTarget (ProjectedCRS)
         eastings = UnitUtils.ConvertFromInternalUnits(eastings, utId);
         northings = UnitUtils.ConvertFromInternalUnits(northings, utId);
         orthogonalHeight = UnitUtils.ConvertFromInternalUnits(orthogonalHeight, utId);

         double dblVal = double.MinValue;
         if (ParameterUtil.GetDoubleValueFromElement(projectInfo, "ProjectGlobalPositioning.Scale", out dblVal) != null && dblVal > MathUtil.Eps())
         {
            scale = dblVal;
         }
         else
         {
            FormatOptions formatOptions = ExporterCacheManager.DocumentUnits.GetFormatOptions(SpecTypeId.Length);
            ForgeTypeId selectedUnitTypeId = formatOptions.GetUnitTypeId();
            if (!utId.Equals(selectedUnitTypeId))
               scale = UnitUtils.Convert(1.0, selectedUnitTypeId, utId);
         }

         IFCAnyHandle mapConversionHnd = IFCInstanceExporter.CreateMapConversion(file, geomRepContext, projectedCRS, eastings, northings,
            orthogonalHeight, xAxisAbscissa, xAxisOrdinate, scale);

         // Assign the main model MapConversion information
         if (ExporterUtil.ExportingHostModel() && 
            !ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         {
            RememberWCSOrGeoReference(eastings, northings, orthogonalHeight, xAxisAbscissa ?? 1.0,
               xAxisOrdinate ?? 0.0);
            CoordReferenceInfo.CrsInfo.GeoRefCRSName = epsgCode;
            CoordReferenceInfo.CrsInfo.GeoRefCRSDesc = crsDescription;
            CoordReferenceInfo.CrsInfo.GeoRefGeodeticDatum = crsGeodeticDatum;
            CoordReferenceInfo.CrsInfo.GeoRefVerticalDatum = crsVerticalDatum;
            CoordReferenceInfo.CrsInfo.GeoRefMapUnit = crsMapUnitStr;
            CoordReferenceInfo.CrsInfo.GeoRefMapProjection = crsMapProjection;
            CoordReferenceInfo.CrsInfo.GeoRefMapZone = crsMapZone;
         }

         return true;
      }

      private bool RememberWCSOrGeoReference (double eastings, double northings, double orthogonalHeight, double xAxisAbscissa, double xAxisOrdinate)
      {
         Transform wcsOrGeoRef = Transform.Identity;
         wcsOrGeoRef.Origin = new XYZ(UnitUtil.UnscaleLength(eastings), UnitUtil.UnscaleLength(northings), UnitUtil.UnscaleLength(orthogonalHeight));
         wcsOrGeoRef.BasisX = new XYZ(xAxisAbscissa, xAxisOrdinate, 0.0);
         wcsOrGeoRef.BasisZ = new XYZ(0.0, 0.0, 1.0);
         wcsOrGeoRef.BasisY = wcsOrGeoRef.BasisZ.CrossProduct(wcsOrGeoRef.BasisX);
         CoordReferenceInfo.MainModelGeoRefOrWCS = wcsOrGeoRef;

         return true;
      }

      /// <summary>
      /// Create IFCSystem from cached items
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="doc">The document to export.</param>
      /// <param name="file">The IFC file.</param>
      /// <param name="systemsCache">The systems to export.</param>
      /// <param name="ownerHistory">The owner history.</param>
      /// <param name="buildingHandle">The building handle.</param>
      /// <param name="projectHasBuilding">Is building exist.</param>
      /// <param name="isElectricalSystem"> Is system electrical.</param>
      private bool ExportCachedSystem(ExporterIFC exporterIFC, Document doc, IFCFile file, IDictionary<ElementId, ISet<IFCAnyHandle>> systemsCache,
         IFCAnyHandle ownerHistory, IFCAnyHandle buildingHandle, bool projectHasBuilding, bool isElectricalSystem)
      {
         bool res = false;
         IDictionary<string, Tuple<string, HashSet<IFCAnyHandle>>> genericSystems = new Dictionary<string, Tuple<string, HashSet<IFCAnyHandle>>>();

         foreach (KeyValuePair<ElementId, ISet<IFCAnyHandle>> system in systemsCache)
         {
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               MEPSystem systemElem = doc.GetElement(system.Key) as MEPSystem;
               if (systemElem == null)
                  continue;

               Element baseEquipment = systemElem.BaseEquipment;
               if (baseEquipment != null)
               {
                  system.Value.AddIfNotNull(ExporterCacheManager.MEPCache.Find(baseEquipment.Id));
               }

               if (isElectricalSystem)
               {
                  // The Elements property below can throw an InvalidOperationException in some cases, which could
                  // crash the export.  Protect against this without having too generic a try/catch block.
                  try
                  {
                     ElementSet members = systemElem.Elements;
                     foreach (Element member in members)
                     {
                        system.Value.AddIfNotNull(ExporterCacheManager.MEPCache.Find(member.Id));
                     }
                  }
                  catch
                  {
                  }
               }

               if (system.Value.Count == 0)
                  continue;

               ElementType systemElemType = doc.GetElement(systemElem.GetTypeId()) as ElementType;
               string name = NamingUtil.GetNameOverride(systemElem, systemElem.Name);
               string desc = NamingUtil.GetDescriptionOverride(systemElem, null);
               string objectType = NamingUtil.GetObjectTypeOverride(systemElem, systemElemType?.Name ?? string.Empty);

               string systemGUID = GUIDUtil.CreateGUID(systemElem);
               IFCAnyHandle systemHandle = null;
               if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
               {
                  systemHandle = IFCInstanceExporter.CreateSystem(file, systemGUID,
                     ownerHistory, name, desc, objectType);
               }
               else
               {
                  string longName = NamingUtil.GetLongNameOverride(systemElem, null);
                  string ifcEnumType;
                  IFCExportInfoPair exportAs = ExporterUtil.GetObjectExportType(systemElem, out ifcEnumType);

                  bool isDistributionCircuit = exportAs.ExportInstance == IFCEntityType.IfcDistributionCircuit;
                  bool isDistributionSystem = exportAs.ExportInstance == IFCEntityType.IfcDistributionSystem;

                  // Only take the predefined type if the export instance matches.
                  string predefinedType = (isDistributionCircuit || isDistributionSystem) ? exportAs.PredefinedType : null;
                  if (predefinedType == null)
                  {
                     Toolkit.IFC4.IFCDistributionSystem systemType = ConnectorExporter.GetMappedIFCDistributionSystemFromElement(systemElem);
                     predefinedType = IFCValidateEntry.ValidateStrEnum<Toolkit.IFC4.IFCDistributionSystem>(systemType.ToString());
                  }

                  if (isDistributionCircuit)
                  {
                     systemHandle = IFCInstanceExporter.CreateDistributionCircuit(file, systemGUID,
                        ownerHistory, name, desc, objectType, longName, predefinedType);

                     string systemName;
                     ParameterUtil.GetStringValueFromElementOrSymbol(systemElem, "IfcDistributionSystem", out systemName);

                     if (!string.IsNullOrEmpty(systemName) && systemHandle != null)
                     {
                        Tuple<string, HashSet<IFCAnyHandle>> circuits = null;
                        if (!genericSystems.TryGetValue(systemName, out circuits))
                        {
                           circuits = new Tuple<string, HashSet<IFCAnyHandle>>(null, new HashSet<IFCAnyHandle>());
                           genericSystems[systemName] = circuits;
                        }
                       
                        // Read PredefinedType for the generic system
                        if (string.IsNullOrEmpty(circuits.Item1))
                        {
                           string genericPredefinedType;
                           ParameterUtil.GetStringValueFromElementOrSymbol(systemElem, "IfcDistributionSystemPredefinedType", out genericPredefinedType);
                           genericPredefinedType = IFCValidateEntry.ValidateStrEnum<Toolkit.IFC4.IFCDistributionSystem>(genericPredefinedType);
                           if (!string.IsNullOrEmpty(genericPredefinedType))
                              genericSystems[systemName] = new Tuple<string, HashSet<IFCAnyHandle>>(genericPredefinedType, circuits.Item2);
                        }
                        // Add the circuit to the generic system
                        circuits.Item2.Add(systemHandle);

                     }
                  }
                  else
                  {
                     systemHandle = IFCInstanceExporter.CreateDistributionSystem(file, systemGUID,
                       ownerHistory, name, desc, objectType, longName, predefinedType);
                  }

               }

               if (systemHandle == null)
                  continue;
               res = true;

               // Create classification reference when System has classification filed name assigned to it
               ClassificationUtil.CreateClassification(exporterIFC, file, systemElem, systemHandle);

               productWrapper.AddSystem(systemElem, systemHandle);

               if (projectHasBuilding)
               {
                  CreateRelServicesBuildings(buildingHandle, file, ownerHistory, systemHandle);
               }

               IFCObjectType? objType = null;
               if (!ExporterCacheManager.ExportOptionsCache.ExportAsCoordinationView2 && ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
                  objType = IFCObjectType.Product;
               string groupGuid = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, systemHandle));
               IFCAnyHandle relAssignsToGroup = IFCInstanceExporter.CreateRelAssignsToGroup(file,
                  groupGuid, ownerHistory, null, null, system.Value, objType, systemHandle);

               ExporterUtil.ExportRelatedProperties(exporterIFC, systemElem, productWrapper);
            }
         }

         foreach (KeyValuePair<string, Tuple<string, HashSet<IFCAnyHandle>>> system in genericSystems)
         {
            string systemGUID = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(IFCEntityType.IfcDistributionSystem, system.Key));
            IFCAnyHandle systemHandle = IFCInstanceExporter.CreateDistributionSystem(file, systemGUID,
                       ownerHistory, system.Key, null, null, null, system.Value.Item1);
            if (systemHandle == null)
               continue;
            
            if (projectHasBuilding)
               CreateRelServicesBuildings(buildingHandle, file, ownerHistory, systemHandle);

            string relGUID = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAggregates, systemHandle));
            IFCInstanceExporter.CreateRelAggregates(file, relGUID, ownerHistory, 
               null, null, systemHandle, system.Value.Item2);
         }

         return res;
      }

      /// <summary>
      /// Create systems from IfcSystem shared parameter of Cable trays and Conduits cached items
      /// </summary>
      /// <param name="doc">The document to export.</param>
      /// <param name="file">The IFC file.</param>
      /// <param name="elementsCache">The systems to export.</param>
      /// <param name="ownerHistory">The owner history.</param>
      /// <param name="buildingHandle">The building handle.</param>
      /// <param name="projectHasBuilding">Is building exist.</param>
      private bool ExportCableTraySystem(Document doc, IFCFile file, ISet<ElementId> elementsCache,
         IFCAnyHandle ownerHistory, IFCAnyHandle buildingHandle, bool projectHasBuilding)
      {
         bool res = false;

         // group elements by IfcSystem string parameter 
         IDictionary<string, ISet<IFCAnyHandle>> groupedSystems = new Dictionary<string, ISet<IFCAnyHandle>>();
         foreach (ElementId elemeId in elementsCache)
         {
            IFCAnyHandle handle = ExporterCacheManager.MEPCache.Find(elemeId);
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(handle))
               continue;

            string systemName;
            Element elem = doc.GetElement(elemeId);
            ParameterUtil.GetStringValueFromElementOrSymbol(elem, "IfcSystem", out systemName);
            if (string.IsNullOrEmpty(systemName))
               continue;

            ISet<IFCAnyHandle> systemElements;
            if (!groupedSystems.TryGetValue(systemName, out systemElements))
            {
               systemElements = new HashSet<IFCAnyHandle>();
               groupedSystems[systemName] = systemElements;
            }
            systemElements.Add(handle);
         }

         // export systems
         foreach (KeyValuePair<string, ISet<IFCAnyHandle>> system in groupedSystems)
         {
            string systemGUID = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(IFCEntityType.IfcSystem, system.Key));
            IFCAnyHandle systemHandle = null;
            if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            {
               systemHandle = IFCInstanceExporter.CreateSystem(file, systemGUID,
                  ownerHistory, system.Key, "", "");
            }
            else
            {
               Toolkit.IFC4.IFCDistributionSystem systemType = Toolkit.IFC4.IFCDistributionSystem.NOTDEFINED;
               string predefinedType = IFCValidateEntry.ValidateStrEnum<Toolkit.IFC4.IFCDistributionSystem>(systemType.ToString());

               systemHandle = IFCInstanceExporter.CreateDistributionSystem(file, systemGUID,
                  ownerHistory, system.Key, "", "", "", predefinedType);
            }

            if (systemHandle == null)
               continue;

            if (projectHasBuilding)
               CreateRelServicesBuildings(buildingHandle, file, ownerHistory, systemHandle);

            IFCObjectType? objType = null;
            if (!ExporterCacheManager.ExportOptionsCache.ExportAsCoordinationView2 && 
               ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
               objType = IFCObjectType.Product;
            string relAssignsGuid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, systemHandle));
            IFCInstanceExporter.CreateRelAssignsToGroup(file, relAssignsGuid,
               ownerHistory, null, null, system.Value, objType, systemHandle);
            res = true;
         }
         return res;
      }

      /// <summary>
      /// Force an immediate garbage collection.
      /// </summary>
      /// <remarks>
      /// Reclaiming the memory occupied by objects that have finalizers implemented 
      /// requires two passes since such objects are placed in the finalization queue 
      /// rather than being reclaimed in the first pass when the garbage collector runs.
      /// </remarks>
      private void ForceGarbageCollection()
      {
         GC.Collect();
         GC.WaitForPendingFinalizers();
         GC.Collect();
         GC.WaitForPendingFinalizers();
      }

      /// <summary>
      /// Get the material name from the material select entity.
      /// </summary>
      /// <param name="materialHnd"> The material handle.</param>
      /// <returns>The material name.</returns>
      private string GetMaterialNameFromMaterialSelect(IFCAnyHandle materialHnd)
      {
         string materialName = IFCAnyHandleUtil.GetStringAttribute(materialHnd, "Name");

         // Special case for IfcMaterialLayerSet
         if (materialName == null)
         {
            materialName = IFCAnyHandleUtil.GetStringAttribute(materialHnd, "LayerSetName");
         }

         // Special case for IfcMaterialProfileSetUsage
         if (materialName == null)   // check 
         {
            IFCAnyHandle materialProfileSet = IFCAnyHandleUtil.GetInstanceAttribute(materialHnd, "ForProfileSet");
            if (materialProfileSet != null)
               materialName = IFCAnyHandleUtil.GetStringAttribute(materialProfileSet, "Name");
         }

         return materialName;
      }
   }
}
