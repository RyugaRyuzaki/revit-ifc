﻿using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;

namespace Revit.IFC.Export.Utility
{
   /// <summary>
   /// ElementType to IFC handle key for dictionary comparer
   /// </summary>
   public sealed class ElementTypeKey : Tuple<ElementType, IFCEntityType, string>
   {
      public ElementTypeKey(ElementType elementType, IFCExportInfoPair exportAs) : base(elementType, exportAs.ExportType, exportAs.GetPredefinedTypeOrDefault()) { }
   }

   /// <summary>
   /// Equality test for the ElementType IFC handle key comparer
   /// </summary>
   internal class TypeHndKeyCompare : IEqualityComparer<ElementTypeKey>
   {
      public bool Equals(ElementTypeKey key1, ElementTypeKey key2)
      {
         if (key1.Item1.Id == key2.Item1.Id
            && key1.Item2 == key2.Item2
            && string.Equals(key1.Item3, key2.Item3, StringComparison.InvariantCultureIgnoreCase))
            return true;
         else
            return false;
      }

      public int GetHashCode(ElementTypeKey key)
      {
         int hash = 23;
         hash = hash * 31 + key.Item1.Id.GetHashCode();
         hash = hash * 31 + (int) key.Item2;
         if (key.Item3 != null)
            hash = hash * 31 + key.Item3.GetHashCode();
         return hash;
      }
   }

   internal class ElementTypeComparer : IEqualityComparer<ElementType>
   {
      public bool Equals(ElementType key1, ElementType key2)
      {
         if (key1.Id == key2.Id)
            return true;
         else
            return false;
      }

      public int GetHashCode(ElementType key)
      {
         int hash = key.Id.GetHashCode();
         return hash;
      }
   }

   /// <summary>
   /// Used to keep a cache of a mapping of an ElementType to a handle.
   /// This is specially handled because the same type can be assigned to different IFC entities 
   /// based on parameter settings.
   /// </summary>
   public class ElementTypeToHandleCache
   {
      static TypeHndKeyCompare keyComparer = new TypeHndKeyCompare();

      /// <summary>
      /// The dictionary mapping from an ElementType to an  handle.
      /// The key is made up by ElementId, IFC entity to export to, and the predefinedtype. PredefinedType will be assigned to a value "NULL" for the default if not specified
      /// </summary>
      private Dictionary<ElementTypeKey, IFCAnyHandle> ElementTypeToHandleDictionary { get; set; } = new(keyComparer);
      private Dictionary<IFCAnyHandle, ElementTypeKey> HandleToElementTypeDictionary { get; set; } = new();
      private HashSet<ElementType> RegisteredElementType { get; set; } = new(new ElementTypeComparer());

      /// <summary>
      /// Finds the handle from the dictionary.
      /// </summary>
      /// <param name="elementId">The element elementId.</param>
      /// <returns>The handle.</returns>
      public IFCAnyHandle Find(ElementType elementType, IFCExportInfoPair exportType)
      {
         IFCAnyHandle handle;
         var key = new ElementTypeKey(elementType, exportType);
         if (ElementTypeToHandleDictionary.TryGetValue(key, out handle))
         {
            return handle;
         }
         return null;
      }

      /// <summary>
      /// Find registered type and predefinedType by a type handle
      /// </summary>
      /// <param name="typeHnd">the type handle</param>
      /// <returns>ElementTypeKey if found</returns>
      public ElementTypeKey Find(IFCAnyHandle typeHnd)
      {
         ElementTypeKey etKey;
         if (HandleToElementTypeDictionary.TryGetValue(typeHnd, out etKey))
         {
            return etKey;
         }
         return null;
      }

      /// <summary>
      /// CHeck whether an ElementType has been registered
      /// </summary>
      /// <param name="elType">the ElementType</param>
      /// <returns>true/false</returns>
      public bool IsRegistered(ElementType elType)
      {
         if (RegisteredElementType.Contains(elType))
            return true;
         return false;
      }

      /// <summary>
      /// Removes invalid handles from the cache.
      /// </summary>
      /// <param name="elementIds">The element ids.</param>
      /// <param name="expectedType">The expected type of the handles.</param>
      public void RemoveInvalidHandles(ISet<ElementTypeKey> keys)
      {
         foreach (ElementTypeKey key in keys)
         {
            IFCAnyHandle handle;
            if (ElementTypeToHandleDictionary.TryGetValue(key, out handle))
            {
               try
               {
                  bool isType = IFCAnyHandleUtil.IsSubTypeOf(handle, key.Item2);
                  if (!isType)
                     ElementTypeToHandleDictionary.Remove(key);
               }
               catch
               {
                  ElementTypeToHandleDictionary.Remove(key);
               }
            }
         }
      }

      /// <summary>
      /// Adds the handle to the dictionary.
      /// </summary>
      /// <param name="elementId">The element elementId.</param>
      /// <param name="handle">The handle.</param>
      public void Register(ElementType elementType, IFCExportInfoPair exportType, IFCAnyHandle handle)
      {
         var key = new ElementTypeKey(elementType, exportType);

         if (ElementTypeToHandleDictionary.ContainsKey(key) || exportType.ExportType == IFCEntityType.UnKnown)
            return;

         ElementTypeToHandleDictionary[key] = handle;
         HandleToElementTypeDictionary[handle] = key;
         RegisteredElementType.Add(key.Item1);
      }

      public void Clear()
      {
         ElementTypeToHandleDictionary.Clear();
         HandleToElementTypeDictionary.Clear();
         RegisteredElementType.Clear();
      }
   }
}
