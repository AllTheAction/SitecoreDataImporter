﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sitecore.SharedSource.DataImporter.Extensions;
using Sitecore.Data.Items;
using Sitecore.Data;
using Sitecore.SharedSource.DataImporter.Mappings.Fields;
using Sitecore.SharedSource.DataImporter.Mappings.Properties;
using System.Web;
using System.Collections;
using Sitecore.SharedSource.DataImporter.Mappings;
using Sitecore.SharedSource.DataImporter.Utility;
using Sitecore.Collections;
using System.IO;
using Sitecore.Data.Fields;
using System.Configuration;
using Sitecore.Globalization;
using Sitecore.Data.Managers;
using Sitecore.SharedSource.DataImporter.Mappings.Templates;
using Sitecore.Diagnostics;
using Sitecore.Layouts;
using Sitecore.SecurityModel;
using Sitecore.SharedSource.DataImporter.Logger;

namespace Sitecore.SharedSource.DataImporter.Providers {
    public class SitecoreDataMap : BaseDataMap {

        #region Static IDs

        /// <summary>
        /// template id of the properties folder
        /// </summary>
        public static readonly string PropertiesFolderTemplateID = "{8452785D-FFE7-47F3-911E-F219F5BDEA3A}";

        /// <summary>
        /// template id of the templates folder
        /// </summary>
        public static readonly string TemplatesFolderTemplateID = "{3D915406-97F6-4E94-AC50-B7CAF468A50F}";

        /// <summary>
        /// template id of the components folder
        /// </summary>
        public static readonly string ComponentsFolderTemplateIdString = "{4E8E2F3D-2327-4BBA-A14F-C586391892CA}";
        public static readonly ID ComponentsFolderTemplateId = new ID(ComponentsFolderTemplateIdString);
        
        #endregion Static IDs

        #region Properties

        private Database _FromDB;
        public Database FromDB {
            get {
                if (_FromDB == null) {
                    var csNames = from ConnectionStringSettings c in ConfigurationManager.ConnectionStrings
                                  where c.ConnectionString.Equals(DatabaseConnectionString)
                                  select c.Name;
                    if (!csNames.Any())
                        throw new NullReferenceException("The database connection string wasn't found.");

                    List<Database> dbs = Sitecore.Configuration.Factory.GetDatabases()
                        .Where(a => a.ConnectionStringName.Equals(csNames.First()))
                        .ToList();

                    if (!dbs.Any())
                        throw new NullReferenceException("No database in the Sitecore configuration using the connection string was found.");

                    _FromDB = dbs.First();
                }
                return _FromDB;
            }
        }

        /// <summary>
        /// List of properties
        /// </summary>
        public List<IBaseProperty> PropertyDefinitions { get; set; }

        /// <summary>
        /// List of properties
        /// </summary>
        public List<IBaseFieldWithReference> ReferenceFieldDefinitions { get; set; }

        /// <summary>
        /// List of template mappings
        /// </summary>
        public Dictionary<string, TemplateMapping> TemplateMappingDefinitions { get; set; }
        /// <summary>
        /// List of template mappings
        /// </summary>
        public IEnumerable<ComponentMapping> ComponentMappingDefinitions { get; set; }
        
        public Item ImportRoot { get; set; }

		public bool DeleteOnOverwrite { get; set; }

		public bool AllowItemNameMatch { get; set; }

		public bool PreserveChildren { get; set; }

        public bool KeepOriginalItemID { get; set; }

        #endregion Properties

        #region Fields

        public Language ImportFromLanguage { get; set; }

        public bool RecursivelyFetchChildren { get; set; }
		public Dictionary<string,string> PathRewrites { get; set; }

        #endregion Fields

        #region Constructor

        public SitecoreDataMap(Database db, string connectionString, Item importItem, ILogger l)
            : base(db, connectionString, importItem, l) {

            //get 'from' language
            ImportFromLanguage = GetImportItemLanguage("Import From Language");

            //get recursive setting
            RecursivelyFetchChildren = ImportItem.GetItemBool("Recursively Fetch Children");

            //populate property definitions
            PropertyDefinitions = GetPropDefinitions(ImportItem);

            ReferenceFieldDefinitions = GetReferenceFieldDefinitions(importItem);

            //populate template definitions
            TemplateMappingDefinitions = GetTemplateDefinitions(ImportItem);

            ComponentMappingDefinitions = GetComponentDefinitions(ImportItem);
            
            ImportRoot = GetImportRootItem();
	        DeleteOnOverwrite = ImportItem.GetItemBool("Delete On Overwrite");
			PreserveChildren = ImportItem.GetItemBool("Preserve Children on Delete");
			AllowItemNameMatch = ImportItem.GetItemBool("Allow Item Name Match");
            KeepOriginalItemID = ImportItem.GetItemBool("Keep Original Item ID");

            PathRewrites = ImportItem.GetItemField("Path Rewrites", Logger)
				.Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
				.ToDictionary(s => s.Split(';')[0], s => s.Split(';')[1]);
		}

        #endregion Constructor

        #region Constructor Helpers

        public Item GetImportRootItem()
        {

            Item toWhere = null;

            //check field value
            string toWhereID = ImportItem.GetItemField("Import Root", Logger);
            if (string.IsNullOrEmpty(toWhereID))
            {
                Logger.Log("SitecoreDataMap.GetImportRootItem", string.Format("the 'Import Root' field is not set on the import item {0}", ImportItem.Paths.FullPath));
                return null;
            }

            //check item
            toWhere = FromDB.Items[toWhereID];
            if (toWhere.IsNull())
                Logger.Log("SitecoreDataMap.GetImportRootItem", string.Format("the 'Import Root' item is null on the import item", ImportItem.Paths.FullPath));

            return toWhere;
        }
        #endregion Constructor Helpers

        #region IDataMap Methods

        /// <summary>
        /// uses the sitecore database and xpath query to retrieve data
        /// </summary>
        /// <returns></returns>
        public override IEnumerable<object> GetImportData() {
			var items = new List<object>();
			foreach (var query in Query.Split(new string[] { Environment.NewLine },StringSplitOptions.RemoveEmptyEntries))
			{
				var cleanQuery = StringUtility.CleanXPath(query);
				Logger.Log("SitecoreDataMap.GetImportData", string.Format("Running query: {0}", cleanQuery));
				items.AddRange(FromDB.SelectItems(cleanQuery));
			}

			return items;
        }

        /// <summary>
        /// deals with the sitecore properties
        /// </summary>
        /// <param name="newItem"></param>
        /// <param name="importRow"></param>
        public override void ProcessCustomData(ref Item newItem, object importRow) {
            Item row = importRow as Item;

            List<IBaseProperty> l = GetPropDefinitionsByRow(importRow);
            
            //add in the property mappings
            foreach (IBaseProperty d in l)
                d.FillField(this, ref newItem, row);

            //recursively get children
            if (RecursivelyFetchChildren)
                ProcessChildren(ref newItem, ref row);
        }

		public void ProcessCustomData(ref Item newItem, object importRow, ComponentMapping mapping)
		{
			Item row = importRow as Item;

			List<IBaseProperty> l = GetComponentPropDefinitionsByRow(importRow, mapping);

			//add in the property mappings
			foreach (IBaseProperty d in l)
				d.FillField(this, ref newItem, row);

			//recursively get children
			if (RecursivelyFetchChildren)
				ProcessChildren(ref newItem, ref row);
		}

		/// <summary>
		/// deals with the sitecore properties
		/// </summary>
		/// <param name="newItem"></param>
		/// <param name="importRow"></param>
		public void ProcessReferenceFields(ref Item newItem, object importRow)
        {
            Item row = importRow as Item;

            List<IBaseFieldWithReference> l = GetReferenceFieldDefinitionsByRow(importRow);

            //add in the property mappings
            foreach (IBaseFieldWithReference d in l)
            {
				try
				{
					var fieldName = d.GetExistingFieldName();
					d.FillField(this, ref newItem, row, fieldName);
				}
				catch (Exception ex)
				{
					Logger.Log("SitecoreDataMap.ProcessReferenceFields", string.Format("the FillField failed for field {1} on item {0}", newItem.Paths.FullPath, d.NewItemField));
				}
			}

            //recursively get children
            if (RecursivelyFetchChildren)
                ProcessChildren(ref newItem, ref row);
        }

		/// <summary>
		/// deals with the sitecore properties
		/// </summary>
		/// <param name="newItem"></param>
		/// <param name="importRow"></param>
		public void ProcessReferenceFields(ref Item newItem, Item row, IEnumerable<IBaseFieldWithReference> referenceFieldMaps)
		{
			

			//add in the property mappings
			foreach (IBaseFieldWithReference d in referenceFieldMaps)
			{
				var fieldName = d.GetExistingFieldName();
				d.FillField(this, ref newItem, row, fieldName);
			}

			//recursively get children
			if (RecursivelyFetchChildren)
				ProcessChildren(ref newItem, ref row);
		}

		/// <summary>
		/// gets a field value from an item
		/// </summary>
		/// <param name="importRow"></param>
		/// <param name="fieldName"></param>
		/// <returns></returns>
		public override string GetFieldValue(object importRow, string fieldName) {
            //check for tokens
            if (fieldName.Equals("$name") || fieldName.Equals("@@name"))
                return ((Item)importRow).Name;

	        if (fieldName.Equals("$parentname"))
		        return ((Item)importRow).Parent.Name;

			Item item = importRow as Item;
            Item langItem = FromDB.GetItem(item.ID, ImportFromLanguage);

            Field f = langItem.Fields[fieldName];
            return (f != null) ? langItem[fieldName] : string.Empty;
        }

        public override CustomItemBase GetNewItemTemplate(object importRow) {

            TemplateMapping tm = GetTemplateMapping((Item)importRow);
            if (tm == null)
                return base.GetNewItemTemplate(importRow);

            BranchItem b = (BranchItem)ToDB.Items[tm.ToWhatTemplate];
            return (CustomItemBase)b;
        }

        public CustomItemBase GetComponentItemTemplate(object importRow, ComponentMapping mapping)
        {

			TemplateMapping tm = GetComponentTemplateMapping((Item)importRow, mapping);
			if (tm == null)
				return base.GetNewItemTemplate(importRow);

			BranchItem b = (BranchItem)ToDB.Items[tm.ToWhatTemplate];
			return (CustomItemBase)b;
		}


        /// <summary>
        /// if a template definition has custom field imports then use that before the global field definitions
        /// </summary>
        /// <param name="importRow"></param>
        /// <returns></returns>
        public override List<IBaseField> GetFieldDefinitionsByRow(object importRow) {

            List<IBaseField> l = new List<IBaseField>();

            //get the template
            TemplateMapping tm = GetTemplateMapping((Item)importRow);
            if (tm == null)
                return FieldDefinitions;

            //get the template fields
            List<IBaseField> tempFields = tm.FieldDefinitions;

            //filter duplicates in template fields from global fields
            List<string> names = tempFields.Select(a => a.Name).ToList();
            l.AddRange(tempFields);
            l.AddRange(FieldDefinitions.Where(a => !names.Contains(a.Name)));

            return l;
        }

        #endregion IDataMap Methods

        #region Methods

        protected virtual void ProcessChildren(ref Item newParent, ref Item oldParent) {
            if (!oldParent.HasChildren)
                return;

            foreach (Item importRow in oldParent.GetChildren()) {

                string newItemName = BuildNewItemName(importRow);
                if (string.IsNullOrEmpty(newItemName))
                    continue;

                CreateNewItem(newParent, importRow, newItemName);
            }
        }

        public override Item CreateNewItem(Item parent, object importRow, string newItemName)
        {
            CustomItemBase nItemTemplate = GetNewItemTemplate(importRow);
			newItemName = RewritePath(newItemName);
            using (new LanguageSwitcher(ImportToLanguage))
            {
                //get the parent in the specific language
                parent = ToDB.GetItem(parent.ID);

                Item newItem;
                //search for the child by name	
				if (AllowItemNameMatch)
				{
					newItem = GetChild(parent, newItemName);
				}
				else
				{
					newItem = ToDB.GetItem(((Item) importRow).ID);
				}
				if (newItem != null) //add version for lang
	            {
		            if (DeleteOnOverwrite)
					{
						newItem = HandleDelete(newItem, newItemName, nItemTemplate, parent, importRow);
					}
		            else
		            {
						if (newItem.ParentID != parent.ID)
						{
							newItem.MoveTo(parent);
						}
			            newItem = newItem.Versions.AddVersion();
		            }
	            }

	            //if not found then create one
                if (newItem == null)
                {
                     newItem = (KeepOriginalItemID)
                        ? ItemManager.AddFromTemplate(newItemName, nItemTemplate.ID, parent, GetItemID((Item) importRow))
                        : ItemManager.AddFromTemplate(newItemName, nItemTemplate.ID, parent);
                }

                if (newItem == null)
                    throw new NullReferenceException("the new item created was null");

                //if found and is default template, change it to the correct one
                if (newItem.TemplateID == ImportToWhatTemplate.ID && nItemTemplate.ID != ImportToWhatTemplate.ID)
                {
                    using (new EditContext(newItem, true, false))
                    {
                        newItem.ChangeTemplate(new TemplateItem(nItemTemplate.InnerItem));
                    }
                }

				using (new EditContext(newItem, true, false))
				{
					ProcessFields(importRow, newItem);
				}
				using (new EditContext(newItem, true, false))
				{
					ProcessReferenceFields(ref newItem, importRow);
				}
				using (new EditContext(newItem, true, false))
				{
					ProcessComponents(newItem, importRow);
				}
				using (new EditContext(newItem, true, false))
				{
					//calls the subclass method to handle custom fields and properties
					ProcessCustomData(ref newItem, importRow);
				}

				Logger.Log("SitecoreDataMap.CreateNewItem", $"Import ID:{((Item) importRow).ID.Guid}, Import Path:{((Item)importRow).Paths.FullPath}, New ID:{newItem.ID.Guid}, New Path:{newItem.Paths.FullPath}");

                return newItem;
            }
        }

		protected virtual ID GetItemID(Item importRow)
		{
			return importRow.ID;
		}

		private Item HandleDelete(Item newItem, string newItemName, CustomItemBase nItemTemplate, Item parent, object importRow)
		{
			if (PreserveChildren)
			{
				var temp = parent.Add("temp", new TemplateID(TemplateIDs.StandardTemplate));
				foreach (Item child in newItem.Children.Where(x => x.TemplateID != ComponentsFolderTemplateId))
				{
					child.MoveTo(temp);
				}
				newItem.Delete();
				newItem = ItemManager.AddFromTemplate(newItemName, nItemTemplate.ID, parent, ((Item) importRow).ID);

				foreach (Item child in temp.Children)
				{
					child.MoveTo(newItem);
				}
				temp.Delete();
			}
			else
			{
				newItem.Delete();
				newItem = null;
			}
			return newItem;
		}

		private void ProcessComponents(Item parent, object importRow)
		{
			IEnumerable<ComponentMapping> componentMappings = GetComponentMappings((Item) importRow);
			ProcessComponents(parent, importRow, componentMappings);
		}
		private void ProcessComponents(Item parent, object importRow, ComponentMapping mapping)
		{
			IEnumerable<ComponentMapping> componentMappings = GetComponentMappings((Item)importRow, mapping);
			ProcessComponents(parent, importRow, componentMappings);
		}
		private void ProcessComponents(Item parent, object importRow, IEnumerable<ComponentMapping> componentMappings)
		{
			using (new LanguageSwitcher(ImportToLanguage))
            {
                //get the parent in the specific language
                foreach (var componentMapping in componentMappings ?? Enumerable.Empty<ComponentMapping>())
				{
					try
					{
						if (!string.IsNullOrEmpty(componentMapping.Rendering) && !string.IsNullOrEmpty(componentMapping.Placeholder))
						{
							CleanRenderings(parent, componentMapping.Rendering, componentMapping.Placeholder);
						}
						var items = componentMapping.GetImportItems((Item) importRow);
						foreach (var item in items)
						{
							if ((componentMapping.RequiredFields.Any()))
							{
								var requiredValues = GetFieldValues(componentMapping.RequiredFields, item);
								if (requiredValues.Any(x => string.IsNullOrEmpty(x)))
								{
									Logger.Log("SitecoreDataMap.ProcessComponents", string.Format("Missing required field for component {0} on item {1}", componentMapping.ComponentName, ((Item) item).ID));
									continue;
								}
							}
							Item folder = parent;
							if (!string.IsNullOrEmpty(componentMapping.FolderName))
							{
								var folderPath = parent.Paths.FullPath + "/" + componentMapping.FolderName;
								folder = ToDB.GetItem(folderPath);
								if (folder == null)
								{
									Logger.Log("SitecoreDataMap.ProcessComponents", string.Format("Could not find component Folder at {0}", folderPath));
									folder = parent;
								}
							}

							
							var nItemTemplate = GetComponentItemTemplate(item, componentMapping);
							Item newItem;
							//search for the child by name
							var name = componentMapping.ComponentName;
							switch (name)
							{
								case "$name":
								case "@@name":
									name = item.Name;
									break;
								case "$outcomePostQuestionName":
									var questionName = item.Name.Replace('P', 'B');
									name = GetChild(folder, questionName) != null ? questionName: item.Name;
									break;
							}
							newItem = GetChild(folder, name);
							if (newItem != null) //add version for lang
							{
								if (componentMapping.PreserveComponentId && newItem.ID != item.ID)
								{
									UpdateReferences(newItem, item.ID);
									newItem.Delete();
									newItem = null;
								}
								else
								{
									newItem = newItem.Versions.AddVersion();
								}
							}
							//if not found then create one
							if (newItem == null)
							{
								if (componentMapping.PreserveComponentId)
								{
									newItem = ItemManager.AddFromTemplate(name, nItemTemplate.ID, folder, item.ID);
								}
								else
								{
									newItem = ItemManager.AddFromTemplate(name, nItemTemplate.ID, folder);
								}
							}

							if (newItem == null)
								throw new NullReferenceException("the new item created was null");

							using (new EditContext(newItem, true, false))
							{
								ProcessFields(item, newItem, componentMapping);
							}

							using (new EditContext(newItem, true, false))
							{

								ProcessReferenceFields(ref newItem, item, componentMapping.ReferenceFieldDefinitions);
							}
							using (new EditContext(newItem, true, false))
							{

								ProcessComponents(newItem, item, componentMapping);
							}
							using (new EditContext(newItem, true, false))
							{
								//calls the subclass method to handle custom fields and properties
								ProcessCustomData(ref newItem, item, componentMapping);
							}
							if (!string.IsNullOrEmpty(componentMapping.Rendering) && !string.IsNullOrEmpty(componentMapping.Placeholder))
							{
								AddRendering(parent, componentMapping.Rendering, componentMapping.Placeholder, newItem);
							}

							Logger.Log("SitecoreDataMap.CreateNewItem", $"Import ID:{item.ID.Guid}, Import Path:{item.Paths.FullPath}, New ID:{newItem.ID.Guid}, New Path:{newItem.Paths.FullPath}");
                        }

					}
					catch (Exception ex)
					{
						Logger.Log("SitecoreDataMap.ProcessComponents", string.Format("failed to import component {0} on item {1}", componentMapping.ComponentName, parent.Paths.FullPath));
					}
				}
            }
        }

		public void CleanRenderings(Item item, string renderingID, string placeholder)
		{
			LayoutField layoutField = new LayoutField(item.Fields[FieldIDs.FinalLayoutField]);
			LayoutDefinition layoutDefinition = LayoutDefinition.Parse(layoutField.Value);
			DeviceDefinition deviceDefinition = layoutDefinition.GetDevice(GetDefaultDeviceItem(item.Database).ID.ToString());

			List<string> placeholders = new List<string>();
			foreach (RenderingDefinition rendering in deviceDefinition.Renderings)
			{
				if (rendering.ItemID.ToLower() == "{C1624533-ED68-41AC-B03C-BFDE4D9B1F2A}".ToLower())
				{
					placeholders.Add(StringUtil.EnsurePrefix('/', rendering.Placeholder + "/" + placeholder + "_" + new Guid(rendering.UniqueId).ToString("D").ToLower())); 
				}
			}
			List<RenderingDefinition> renderingsToRemove = new List<RenderingDefinition>();
			foreach (RenderingDefinition rendering in deviceDefinition.Renderings)
			{
				if (rendering.ItemID.ToLower() == renderingID.ToLower() && placeholders.Contains(rendering.Placeholder))
				{
					renderingsToRemove.Add(rendering);
				}
			}
			foreach (var rendering in renderingsToRemove)
			{
				deviceDefinition.Renderings.Remove(rendering);
			}
			using (new SecurityDisabler())
			{
				item.Editing.BeginEdit();
				layoutField.Value = layoutDefinition.ToXml();
				item.Editing.EndEdit();
			}
		}

		public void AddRendering(Item item, string renderingID, string placeholder, Item datasource)
		{
			LayoutField layoutField = new LayoutField(item.Fields[FieldIDs.FinalLayoutField]);
			LayoutDefinition layoutDefinition = LayoutDefinition.Parse(layoutField.Value);
			DeviceDefinition deviceDefinition = layoutDefinition.GetDevice(GetDefaultDeviceItem(item.Database).ID.ToString());
			
			foreach (RenderingDefinition rendering in deviceDefinition.Renderings)
			{
				if (rendering.ItemID.ToLower() == "{C1624533-ED68-41AC-B03C-BFDE4D9B1F2A}".ToLower())
				{
					placeholder= StringUtil.EnsurePrefix('/', rendering.Placeholder + "/" + placeholder + "_" + new Guid(rendering.UniqueId).ToString("D").ToLower());
					break;
				}
			}
			var newRendering = new RenderingDefinition
			{
				Placeholder = placeholder,
				ItemID = renderingID,
				Datasource = datasource.ID.ToString()
			};
			deviceDefinition.AddRendering(newRendering);
			using (new SecurityDisabler())
			{
				item.Editing.BeginEdit();
				layoutField.Value = layoutDefinition.ToXml();
				item.Editing.EndEdit();
			}
		}

		private DeviceItem GetDefaultDeviceItem(Database db)
		{
			return db.Resources.Devices.GetAll().First(d => d.IsDefault);
		}
		private void ProcessFields(object importRow, Item newItem)
        {
            //add in the field mappings
            List<IBaseField> fieldDefs = GetFieldDefinitionsByRow(importRow);
            ProcessFields(importRow, newItem, fieldDefs);
        }

		private void ProcessFields(object importRow, Item newItem, ComponentMapping mapping)
		{
			//add in the field mappings
			List<IBaseField> fieldDefs = GetComponentFieldDefinitionsByRow(importRow, mapping);
			ProcessFields(importRow, newItem, fieldDefs);
		}

		private void ProcessFields(object importRow, Item newItem, List<IBaseField> fieldDefs)
		{
			//add in the field mappings
			foreach (IBaseField d in fieldDefs)
			{
				string importValue = string.Empty;
				try
				{
					IEnumerable<string> values = GetFieldValues(d.GetExistingFieldNames(), importRow);
					importValue = String.Join(d.GetFieldValueDelimiter(), values);
					d.FillField(this, ref newItem, importValue);
				}
				catch (Exception ex)
				{
					Logger.Log("SitecoreDataMap.ProcessFields", string.Format("the FillField failed for field {1} on item {0}", newItem.Paths.FullPath, d.Name));
				}
			}
		}
		protected TemplateMapping GetTemplateMapping(Item item) {
            string tID = item.TemplateID.ToString();
            return (TemplateMappingDefinitions.ContainsKey(tID))
                ? TemplateMappingDefinitions[tID]
                : null;
        }

		protected TemplateMapping GetComponentTemplateMapping(Item item, ComponentMapping mapping)
		{
			string tID = item.TemplateID.ToString();
			return (mapping.TemplateMappingDefinitions.ContainsKey(tID))
				? mapping.TemplateMappingDefinitions[tID]
				: null;
		}

		protected IEnumerable<ComponentMapping> GetComponentMappings(Item item)
        {
            string tID = item.TemplateID.ToString();
            return ComponentMappingDefinitions;
        }

		protected IEnumerable<ComponentMapping> GetComponentMappings(Item item, ComponentMapping mapping)
		{
			return mapping.ComponentMappingDefinitions;
		}


		protected List<IBaseProperty> GetPropDefinitionsByRow(object importRow) {
            List<IBaseProperty> l = new List<IBaseProperty>();
            TemplateMapping tm = GetTemplateMapping((Item)importRow);
            if (tm == null) 
                return PropertyDefinitions;

            //get the template fields
            List<IBaseProperty> tempProps = tm.PropertyDefinitions;

            //filter duplicates in template fields from global fields
            List<string> names = tempProps.Select(a => a.Name).ToList();
            l.AddRange(tempProps);
            l.AddRange(PropertyDefinitions.Where(a => !names.Contains(a.Name)));
            
            return l;
        }

		protected List<IBaseProperty> GetComponentPropDefinitionsByRow(object importRow, ComponentMapping mapping)
		{
			List<IBaseProperty> l = new List<IBaseProperty>();
			TemplateMapping tm = GetTemplateMapping((Item)importRow);
			if (tm == null)
				return mapping.PropertyDefinitions;

			//get the template fields
			List<IBaseProperty> tempProps = tm.PropertyDefinitions;

			//filter duplicates in template fields from global fields
			List<string> names = tempProps.Select(a => a.Name).ToList();
			l.AddRange(tempProps);
			l.AddRange(mapping.PropertyDefinitions.Where(a => !names.Contains(a.Name)));

			return l;
		}

		protected List<IBaseFieldWithReference> GetReferenceFieldDefinitionsByRow(object importRow)
        {
            List<IBaseFieldWithReference> l = new List<IBaseFieldWithReference>();
            TemplateMapping tm = GetTemplateMapping((Item)importRow);
            if (tm == null)
                return ReferenceFieldDefinitions;

            //get the template fields
            List<IBaseFieldWithReference> tempProps = tm.ReferenceFieldDefinitions;

            //filter duplicates in template fields from global fields
            List<string> names = tempProps.Select(a => a.Name).ToList();
            l.AddRange(tempProps);
            l.AddRange(ReferenceFieldDefinitions.Where(a => !names.Contains(a.Name)));

            return l;
        }

		protected List<IBaseFieldWithReference> GetComponentReferenceFieldDefinitionsByRow(object importRow, ComponentMapping mapping)
		{
			List<IBaseFieldWithReference> l = new List<IBaseFieldWithReference>();
			TemplateMapping tm = GetTemplateMapping((Item)importRow);
			if (tm == null)
				return mapping.ReferenceFieldDefinitions;

			//get the template fields
			List<IBaseFieldWithReference> tempProps = tm.ReferenceFieldDefinitions;

			//filter duplicates in template fields from global fields
			List<string> names = tempProps.Select(a => a.Name).ToList();
			l.AddRange(tempProps);
			l.AddRange(mapping.ReferenceFieldDefinitions.Where(a => !names.Contains(a.Name)));

			return l;
		}
		protected List<IBaseField> GetComponentFieldDefinitionsByRow(object importRow, ComponentMapping mapping)
		{
			List<IBaseField> l = new List<IBaseField>();
			TemplateMapping tm = GetComponentTemplateMapping((Item)importRow, mapping);
			if (tm?.FieldDefinitions == null)
				return mapping.FieldDefinitions;

			//get the template fields
			List<IBaseField> tempProps = tm.FieldDefinitions;

			//filter duplicates in template fields from global fields
			List<string> names = tempProps.Select(a => a.Name).ToList();
			l.AddRange(tempProps);
			l.AddRange(mapping.FieldDefinitions.Where(a => !names.Contains(a.Name)));

			return l;
		}

		protected List<IBaseProperty> GetPropDefinitions(Item i) {

            List<IBaseProperty> l = new List<IBaseProperty>();

            //check for properties folder
            Item Props = i.GetChildByTemplate(PropertiesFolderTemplateID);
            if (Props.IsNull()) {
                Logger.Log("Warn", string.Format("there is no 'Properties' folder on '{0}'", i.DisplayName));
                return l;
            }

            //check for any children
            if (!Props.HasChildren) {
                Logger.Log("Warn", string.Format("there are no properties to import on '{0}'", i.DisplayName));
                return l;
            }

            ChildList c = Props.GetChildren();
            foreach (Item child in c) {
                //create an item to get the class / assembly name from
                BaseMapping bm = new BaseMapping(child);

				//check for assembly
				if (string.IsNullOrEmpty(bm.HandlerAssembly))
				{
					Logger.Log("SitecoreDataMap.GetPropDefinitions", string.Format("the field's Handler Assembly is not defined on item {0}: {1}", child.Paths.FullPath, bm.HandlerAssembly));
					continue;
				}

				//check for class
				if (string.IsNullOrEmpty(bm.HandlerClass))
				{
					Logger.Log("SitecoreDataMap.GetPropDefinitions", string.Format("the field's Handler Class is not defined on item {0}: {1}", child.Paths.FullPath, bm.HandlerClass));
					continue;
				}

				//create the object from the class and cast as base field to add it to field definitions
				IBaseProperty bp = null;
				try
				{
					bp = (IBaseProperty)Sitecore.Reflection.ReflectionUtil.CreateObject(bm.HandlerAssembly, bm.HandlerClass, new object[] { child, Logger });
				}
				catch (FileNotFoundException)
				{
					Logger.Log("SitecoreDataMap.GetPropDefinitions", string.Format("the field's binary specified could not be found on item {0} : {1}", child.Paths.FullPath, bm.HandlerAssembly));
				}

                if (bp != null)
                    l.Add(bp);
                else
                    Logger.Log(child.Paths.FullPath, "the class type could not be instantiated", ProcessStatus.ImportDefinitionError, "Handler Class", bm.HandlerClass);
            }

            return l;
        }

        protected Dictionary<string, TemplateMapping> GetTemplateDefinitions(Item i) {

            Dictionary<string, TemplateMapping> d = new Dictionary<string, TemplateMapping>();

            //check for templates folder
            Item Temps = i.GetChildByTemplate(TemplatesFolderTemplateID);
            if (Temps.IsNull()) {
                Logger.Log("Warn", string.Format("there is no 'Templates' folder on '{0}'", i.DisplayName));
				TemplateMapping tm = new TemplateMapping(i);
				d.Add(tm.FromWhatTemplate, tm);
				return d;
            }

            //check for any children
            if (!Temps.HasChildren) {
                Logger.Log("Warn", string.Format("there are no templates mappings to import on '{0}'", i.DisplayName));
                return d;
            }

            ChildList c = Temps.GetChildren();
            foreach (Item child in c) {
                //create an item to get the class / assembly name from
                TemplateMapping tm = new TemplateMapping(child);
                tm.FieldDefinitions = GetFieldDefinitions(child);
                tm.PropertyDefinitions = GetPropDefinitions(child);
                tm.ReferenceFieldDefinitions = GetReferenceFieldDefinitions(child);

                //check for 'from' template
                if (string.IsNullOrEmpty(tm.FromWhatTemplate)) {
                    Logger.Log(child.Paths.FullPath, "the template mapping field 'FromWhatTemplate' is not defined", ProcessStatus.ImportDefinitionError, "From What Template");
                    continue;
                }

                //check for 'to' template
                if (string.IsNullOrEmpty(tm.ToWhatTemplate)) {
                    Logger.Log(child.Paths.FullPath, "the template mapping field 'ToWhatTemplate' is not defined", ProcessStatus.ImportDefinitionError, "To What Template");
                    continue;
                }

                d.Add(tm.FromWhatTemplate, tm);
            }

            return d;
        }

        protected IEnumerable<ComponentMapping> GetComponentDefinitions(Item i)
        {

            List<ComponentMapping> d = new List<ComponentMapping>();

            //check for templates folder
            Item Temps = i.GetChildByTemplate(ComponentsFolderTemplateIdString);
            if (Temps.IsNull())
            {
                Logger.Log("SitecoreDataMap.GetComponentDefinitions", string.Format("there is no 'Components' folder on '{0}'", i.DisplayName));
                return d;
            }

            //check for any children
            if (!Temps.HasChildren)
            {
                Logger.Log("SitecoreDataMap.GetComponentDefinitions", string.Format("there are no component mappings to import on '{0}'", i.DisplayName));
                return d;
            }

            ChildList c = Temps.GetChildren();
            foreach (Item child in c)
            {
                //create an item to get the class / assembly name from
                ComponentMapping tm = new ComponentMapping(child, Logger);
                tm.FieldDefinitions = GetFieldDefinitions(child);
				tm.ReferenceFieldDefinitions = GetReferenceFieldDefinitions(child);
				tm.PropertyDefinitions = GetPropDefinitions(child);
				tm.TemplateMappingDefinitions = GetTemplateDefinitions(child);
				tm.ComponentMappingDefinitions = GetComponentDefinitions(child).ToList();

				//check for 'from' template
				if (string.IsNullOrEmpty(tm.FromWhatTemplate))
				{
					Logger.Log("SitecoreDataMap.GetComponentDefinitions", string.Format("the template mapping field 'FromWhatTemplate' is not defined on import row {0}", child.Paths.FullPath));
					continue;
				}

				//check for 'to' template
				if (string.IsNullOrEmpty(tm.ToWhatTemplate))
				{
					Logger.Log("SitecoreDataMap.GetComponentDefinitions", string.Format("the template mapping field 'ToWhatTemplate' is not defined", child.Paths.FullPath));
					continue;
				}

				d.Add(tm);
            }

            return d;
        }

        protected virtual List<IBaseFieldWithReference> GetReferenceFieldDefinitions(Item i)
        {
            List<IBaseFieldWithReference> l = new List<IBaseFieldWithReference>();

            //check for fields folder
            Item Fields = i.GetChildByTemplate(ReferenceFieldsFolderTemplateID);
            if (Fields.IsNull())
            {
                Logger.Log("BaseDataMap.GetReferenceFieldDefinitions", string.Format("there is no 'Reference Fields' folder on the import item {0}", i.Paths.FullPath));
                return l;
            }

            //check for any children
            if (!Fields.HasChildren)
            {
                Logger.Log("BaseDataMap.GetReferenceFieldDefinitions", string.Format("there are no reference fields to import on  on the import item {0}", ImportItem.Paths.FullPath));
                return l;
            }

            ChildList c = Fields.GetChildren();
            foreach (Item child in c)
            {
                //create an item to get the class / assembly name from
                BaseMapping bm = new BaseMapping(child);

                var fieldRef = GenerateType<IBaseFieldWithReference>(child, bm.HandlerClass, bm.HandlerAssembly, new object[] { child });
                if (fieldRef != null)
                    l.Add(fieldRef);
            }

            return l;
        }

        public override Item GetParentNode(object importRow, string newItemName)
        {
            var item = base.GetParentNode(importRow, newItemName);
            if (FolderByPath)
            {
                var newPath = ((Item)importRow).Paths.Path;
                newPath = newPath.Replace(ImportRoot.Paths.Path, ImportToWhere.Paths.Path);
                newPath = newPath.Substring(0, newPath.LastIndexOf("/"));
                newPath = string.Join("/", newPath.Split('/')
											.Select(s => RewritePath(s))
											.Select(s => StringUtility.StripInvalidChars(s))
											.Where(s => !string.IsNullOrEmpty(s)));
                newPath = StringUtil.EnsurePrefix('/', newPath); 
                item = GetPathParentNode(newPath, ((Item)importRow).Parent);
            }

            return item;
        }

		private string RewritePath(string s)
		{
			if (PathRewrites.ContainsKey(s))
			{
				return PathRewrites[s];
			}
			return s;
		}
        protected Item GetPathParentNode(string path, Item sourceItem)
        {
            if(!path.Contains(ImportRoot.Paths.Path))
            {
                Logger.Log("SitecoreDataMap.GetPathParentNode", string.Format("Imported Item {0} is not under the Import Root, moving to top level", path));
                return ImportToWhere;
            }
            
            var item = ToDB.GetItem(path);
            if (item == null)
            {
                var parentPath = path.Substring(0, path.LastIndexOf("/"));
                var itemName = path.Substring(path.LastIndexOf("/") + 1);
                var parent = GetPathParentNode(parentPath, sourceItem.Parent);

                // If the template mapping for this parent item is different, change the template
                item = parent.Add(itemName, new TemplateID(GetNewItemTemplate(sourceItem).ID));
               
            }
            return item;
        }


        #endregion Methods

		
		private void UpdateReferences(Item oldItem, ID newId) {
			var links = Sitecore.Globals.LinkDatabase.GetItemReferrers(oldItem, true);
			using (new Sitecore.SecurityModel.SecurityDisabler())
		{
			foreach (var link in links)
			{
				var sourceItem = link.GetSourceItem();
				var fieldId = link.SourceFieldID;
				var field = sourceItem.Fields[fieldId];

				sourceItem.Editing.BeginEdit();

				try
				{
					field.Value = field.Value
						.Replace(oldItem.ID.ToString(), newId.ToString());
				}
				catch
				{
					sourceItem.Editing.CancelEdit();
				}
				finally
				{
					sourceItem.Editing.EndEdit();
				}
			}
		}
		}
    }
}
