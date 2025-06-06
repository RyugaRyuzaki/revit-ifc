﻿//
// Revit IFC Import library: this library works with Autodesk(R) Revit(R) to import IFC files.
// Copyright (C) 2013  Autodesk, Inc.
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

namespace Revit.IFC.Import.Enums
{
   /// <summary>
   /// IFC supported schema versions, ordered by date.
   /// </summary>
   public enum IFCSchemaVersion
   {
      IFC2x,
      IFC2x2,
      IFC2x3,
      // We cannot distinguish between obsolete IFC4 pre-Add2 files and IFC4Add2 files.
      // As such, all files are marked as IFC4 until we find some evidence that they 
      // are older formats, in which case we can "downgrade" the version.
      IFC4Obsolete,
      IFC4Add1Obsolete,
      IFC4,
      IFC4x1,
      IFC4x2,
      IFC4x3_RC1,
      IFC4x3_RC4,
      IFC4x3,
      IFC4x3_ADD2
   }
}