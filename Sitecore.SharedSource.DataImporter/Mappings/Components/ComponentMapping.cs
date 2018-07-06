using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sitecore.Data.Items;
using Sitecore.SharedSource.DataImporter.Extensions;
using Sitecore.SharedSource.DataImporter.Mappings.Fields;
using Sitecore.SharedSource.DataImporter.Mappings.Properties;
using Sitecore.SharedSource.DataImporter.Utility;
using Sitecore.Data;
using Sitecore.SharedSource.DataImporter.Mappings.Templates;
using Sitecore.SharedSource.DataImporter.Logger;
using Sitecore.SharedSource.DataImporter.Providers;

namespace Sitecore.SharedSource.DataImporter.Mappings.Components {
	public class ComponentMapping {

		#region Properties

		/// <summary>
		/// the template the old item is from
		/// </summary>
        public string FromWhatTemplate { get; set; }

		/// <summary>
		/// the template the new item is going to
		/// </summary>
        public string ToWhatTemplate { get; set; }

        /// <summary>
        /// the name of the new item
        /// </summary>
        public string ComponentName { get; set; }
		/// <summary>
		/// the name of the component's folder
		/// </summary>
		public string FolderName { get; set; }
		/// <summary>
		/// the name of the component's folder
		/// </summary>
		public IEnumerable<string> RequiredFields { get; set; }

		/// <summary>
		/// the definitions of fields to import
		/// </summary>
		public List<IBaseField> FieldDefinitions { get; set; }
		/// <summary>
		/// the definitions of fields to import
		/// </summary>
		public List<IBaseFieldWithReference> ReferenceFieldDefinitions { get; set; }
		public IEnumerable<ComponentMapping> ComponentMappingDefinitions { get; set; }

		public Dictionary<string,TemplateMapping> TemplateMappingDefinitions { get; set; }
		public TemplateID ComponentFolderTemplateID { get; set; }

        /// <summary>
        /// List of properties
        /// </summary>
        public List<IBaseProperty> PropertyDefinitions { get; set; }
		public string Query { get; set; }
		public string Rendering { get; set; }
		public bool PreserveComponentId { get; set; }
		public string Placeholder { get; set; }
		public ILogger Logger { get; set; }
        
		#endregion

        //constructor
        public ComponentMapping(Item i, ILogger logger) {
            FromWhatTemplate = i.Fields["From What Template"].Value;
			ToWhatTemplate = i.Fields["To What Template"].Value;
            ComponentName = i.Fields["Component Name"].Value;
			FolderName = i.Fields["Folder Name"].Value;
			RequiredFields = i.Fields["Required Fields"].Value?.Split(new []{';'}, StringSplitOptions.RemoveEmptyEntries);
			Rendering = i.Fields["Rendering"].Value;
			Query = i.Fields["Query"].Value;
			PreserveComponentId = i.GetItemBool("Preseve Component ID");
			Placeholder = i.Fields["Placeholder"].Value;
			ComponentFolderTemplateID = new TemplateID(new ID(i.Fields["Folder Template"].Value));
			Logger = logger;
		}

		public virtual Item[] GetImportItems(Item parent)
		{
			var items = new List<Item>();
			foreach(var queryLine in Query.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
			{
				var query = queryLine;
				if (string.IsNullOrEmpty(query))
				{
					return new[] {parent};
				}
				if (query == "../")
				{
					items.Add(parent.Parent);
					continue;
				}
				if (query == "./")
				{
					items.Add(parent);
					continue;
				}
				if (query == "./*")
				{
					items.AddRange(parent.Children);
					continue;
				}
				if (query.StartsWith("./")) 
				{
					query = query.Replace("./", StringUtil.EnsurePostfix('/', parent.Paths.FullPath));
				}

				var cleanQuery = StringUtility.CleanXPath(query);
				Logger.Log("ComponentMapping.GetImportItems", string.Format("Running query: {0}", cleanQuery));
				items.AddRange(parent.Database.SelectItems(cleanQuery));
			}
			return items.ToArray();
		}

		public virtual Item GetFolder(Item item, Item parent, Item importRow)
		{
			Item folder = parent;
			if (FolderName == "$path")
			{
				var folderPath = item.Paths.ParentPath.Replace(((Item)importRow).Paths.FullPath, parent.Paths.FullPath);
				folder = parent.Database.GetItem(folderPath);
				if (folder == null)
				{
					Logger.Log(string.Format("Could not find component Folder at {0}", folderPath), null, ProcessStatus.Warning);
					folder = parent;
				}

			}
			if (FolderName == "$parentpath")
			{
				folder = parent.Parent;
			}
			else if (!string.IsNullOrEmpty(FolderName))
			{
				var folderPath = parent.Paths.FullPath + "/" + FolderName;
				folder = GetPathFolderNode(folderPath, parent);
				if (folder == null)
				{
					Logger.Log(string.Format("Could not find component Folder at {0}", folderPath), null, ProcessStatus.Warning);
					folder = parent;
				}
			}
			return folder;
		}
		protected virtual Item GetPathFolderNode(string path, Item sourceItem)
		{

			var item = sourceItem.Database.GetItem(path);
			if (item == null)
			{
				var parentPath = path.Substring(0, path.LastIndexOf("/"));
				var itemName = path.Substring(path.LastIndexOf("/") + 1);
				var parent = GetPathFolderNode(parentPath, sourceItem);
				
				item = parent.Add(itemName, ComponentFolderTemplateID);

			}
			return item;
		}
	}
}
