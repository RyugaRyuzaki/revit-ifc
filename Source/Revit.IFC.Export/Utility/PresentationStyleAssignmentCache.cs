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
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;

namespace Revit.IFC.Export.Utility
{
   /// <summary>
   /// Used to keep a cache of the IfcPresentationStyleAssignment handles mapping to a text element type in Revit.
   /// </summary>
   public class PresentationStyleAssignmentCache
   {
      /// <summary>
      /// The dictionary mapping from a text element type to an IfcPresentationStyleAssignment handle. 
      /// </summary>
      private Dictionary<ElementId, IFCAnyHandle> Styles { get; set; } = new();

      /// <summary>
      /// Constructs a default PresentationStyleAssignmentCache object.
      /// </summary>
      public PresentationStyleAssignmentCache()
      {
      }

      /// <summary>
      /// Finds the IfcPresentationStyleAssignment handle for a particular material.
      /// </summary>
      /// <param name="elementId">The Material's element id.</param>
      /// <returns>The IfcPresentationStyleAssignment handle.</returns>
      public IFCAnyHandle Find(ElementId materialId)
      {
         IFCAnyHandle presentationStyleAssignment;
         if (Styles.TryGetValue(materialId, out presentationStyleAssignment))
         {
            // Make sure the handle isn't stale.
            try
            {
               bool isPSA = (IFCAnyHandleUtil.IsSubTypeOf(presentationStyleAssignment, IFCEntityType.IfcPresentationStyleAssignment)
                          || IFCAnyHandleUtil.IsSubTypeOf(presentationStyleAssignment, IFCEntityType.IfcPresentationStyle));
               if (isPSA)
                  return presentationStyleAssignment;
            }
            catch
            {
            }

            Styles.Remove(materialId);
         }

         return null;
      }

      /// <summary>
      /// Adds the IfcPresentationStyleAssignment handle to the dictionary.
      /// </summary>
      /// <param name="elementId">The element elementId.</param>
      /// <param name="handle">The IfcPresentationStyleAssignment handle.</param>
      public void Register(ElementId elementId, IFCAnyHandle handle)
      {
         if (Styles.ContainsKey(elementId))
            throw new Exception("TextStyleCache already contains handle for elementId " + elementId);

         if (!IFCAnyHandleUtil.IsNullOrHasNoValue(handle))
            Styles[elementId] = handle;
         else
            throw new Exception("Invalid Handle.");
      }

      public void Clear()
      {
         Styles.Clear();
      }
   }
}