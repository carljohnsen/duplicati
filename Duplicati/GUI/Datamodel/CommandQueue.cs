#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
#endregion
/// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=C:\Documents and Settings\Kenneth\Dokumenter\duplicati\Duplicati\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>CommandQueue</name>
/// <sql></sql>
/// </metadata>

namespace Duplicati.Datamodel
{

	public partial class CommandQueue : System.Data.LightDatamodel.DataClassBase
	{

#region " private members "

		[System.Data.LightDatamodel.MemberModifierAutoIncrement()]
		private System.Int64 m_ID = 0;
		private System.String m_Command = "";
		private System.String m_Argument = "";
		private System.Boolean m_Completed = false;
#endregion

#region " unique value "

		public override object UniqueValue {get{return m_ID;}}
		public override string UniqueColumn {get{return "ID";}}
#endregion

#region " properties "

		public System.Int64 ID
		{
			get{return m_ID;}
			set{object oldvalue = m_ID;OnBeforeDataChange(this, "ID", oldvalue, value);m_ID = value;OnAfterDataChange(this, "ID", oldvalue, value);}
		}

		public System.String Command
		{
			get{return m_Command;}
			set{object oldvalue = m_Command;OnBeforeDataChange(this, "Command", oldvalue, value);m_Command = value;OnAfterDataChange(this, "Command", oldvalue, value);}
		}

		public System.String Argument
		{
			get{return m_Argument;}
			set{object oldvalue = m_Argument;OnBeforeDataChange(this, "Argument", oldvalue, value);m_Argument = value;OnAfterDataChange(this, "Argument", oldvalue, value);}
		}

		public System.Boolean Completed
		{
			get{return m_Completed;}
			set{object oldvalue = m_Completed;OnBeforeDataChange(this, "Completed", oldvalue, value);m_Completed = value;OnAfterDataChange(this, "Completed", oldvalue, value);}
		}

#endregion

#region " referenced properties "

#endregion

	}

}