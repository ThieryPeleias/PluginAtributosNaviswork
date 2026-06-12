using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace Virtuart4DNavisworks
{
    public enum AttributeSetMode
    {
        SelectionSet,
        SearchSet
    }

    public sealed class AttributePropertyInfo
    {
        public string Category { get; set; }
        public string Property { get; set; }

        public AttributePropertyInfo(string category, string property)
        {
            Category = category;
            Property = property;
        }
    }

    public sealed class AttributeSetBuildResult
    {
        public string Value { get; set; }
        public string SetName { get; set; }
        public string Mode { get; set; }
        public int ItemCount { get; set; }
        public bool Created { get; set; }
        public bool Reused { get; set; }
        public bool Skipped { get; set; }
        public string Message { get; set; }
    }

    public static class AttributeSetBuilderService
    {
        public static List<AttributePropertyInfo> DiscoverProperties(Document doc)
        {
            var properties = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in EnumerateModelItems(doc))
            {
                if (item?.PropertyCategories == null)
                    continue;

                foreach (PropertyCategory category in item.PropertyCategories)
                {
                    var categoryName = GetDisplayName(category);
                    if (string.IsNullOrWhiteSpace(categoryName))
                        continue;

                    if (!properties.TryGetValue(categoryName, out var propertyNames))
                    {
                        propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        properties[categoryName] = propertyNames;
                    }

                    foreach (DataProperty property in category.Properties)
                    {
                        var propertyName = GetDisplayName(property);
                        if (!string.IsNullOrWhiteSpace(propertyName))
                            propertyNames.Add(propertyName);
                    }
                }
            }

            return properties
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(kvp => kvp.Value
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Select(propertyName => new AttributePropertyInfo(kvp.Key, propertyName)))
                .ToList();
        }

        public static List<AttributeSetBuildResult> Build(
            Document doc,
            string category,
            string property,
            AttributeSetMode mode,
            IProgress<string> progress = null)
        {
            if (doc == null || doc.IsClear)
                throw new InvalidOperationException("Open a Navisworks document before creating attribute sets.");

            category = CleanName(category);
            property = CleanName(property);

            if (string.IsNullOrWhiteSpace(category))
                throw new InvalidOperationException("Choose a category.");

            if (string.IsNullOrWhiteSpace(property))
                throw new InvalidOperationException("Choose a property.");

            var root = doc.SelectionSets?.RootItem;
            if (root == null)
                throw new InvalidOperationException("Selection Sets are not available in this document.");

            // NOTE: In Navisworks API, SelectionSets are always read-only when accessed directly.
            // We must use doc.SelectionSets.InsertCopy / ReplaceWithCopy to modify them.

            progress?.Report($"Scanning {category} / {property}...");

            var groups = GroupItemsByValue(doc, category, property);
            if (groups.Count == 0)
                return new List<AttributeSetBuildResult>();

            var categoryFolder = GetOrCreateFolder(doc, root, category);
            var propertyFolder = GetOrCreateFolder(doc, categoryFolder, property);

            var results = new List<AttributeSetBuildResult>();
            var index = 0;

            foreach (var group in groups.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                index++;
                progress?.Report($"Creating set {index}/{groups.Count}: {group.Key}");

                // After each InsertCopy/ReplaceWithCopy, the tree may be refreshed.
                // Re-locate the property folder from the live document tree.
                var liveCategoryFolder = FindGroupItemRecursive(doc.SelectionSets.RootItem, category);
                if (liveCategoryFolder != null)
                {
                    var livePropertyFolder = FindChild(liveCategoryFolder, property) as FolderItem;
                    if (livePropertyFolder != null)
                        propertyFolder = livePropertyFolder;
                }

                results.Add(CreateOrUpdateSet(doc, propertyFolder, group.Value, mode, category, property));
            }

            progress?.Report($"Finished: {results.Count(r => r.Created)} created, {results.Count(r => r.Reused)} updated.");
            return results;
        }

        private static Dictionary<string, GroupedValue> GroupItemsByValue(Document doc, string category, string property)
        {
            var groups = new Dictionary<string, GroupedValue>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<ModelItem>();
            var count = 0;

            foreach (var item in EnumerateModelItems(doc))
            {
                count++;
                if (item == null || seen.Contains(item))
                    continue;

                if (!TryGetProperty(item, category, property, out var navisProperty))
                    continue;

                var value = FormatValue(navisProperty.Value);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (!groups.TryGetValue(value, out var group))
                {
                    group = new GroupedValue(value);
                    groups[value] = group;
                }

                group.Items.Add(item);
                group.Samples.Add(navisProperty.Value);
                seen.Add(item);
            }

            System.Diagnostics.Debug.WriteLine($"[Virtuart4D] Scanned {count} model items for attribute sets.");
            return groups;
        }

        private static AttributeSetBuildResult CreateOrUpdateSet(
            Document doc,
            GroupItem parent,
            GroupedValue group,
            AttributeSetMode mode,
            string category,
            string property)
        {
            var setName = CleanName(group.Value);
            var existing = FindChild(parent, setName);
            var result = new AttributeSetBuildResult
            {
                Value = group.Value,
                SetName = setName,
                Mode = mode == AttributeSetMode.SearchSet ? "Search" : "Set",
                ItemCount = group.Items.Count
            };

            if (existing is SelectionSet)
            {
                // Update existing set via ReplaceWithCopy
                var existingIndex = FindChildIndex(parent, setName);
                if (existingIndex < 0)
                {
                    result.Skipped = true;
                    result.Message = "Could not locate existing set index.";
                    return result;
                }

                SelectionSet updatedSet;
                if (mode == AttributeSetMode.SearchSet)
                {
                    var search = CreateSearch(doc, category, property, group.Value, group.Samples);
                    updatedSet = new SelectionSet(search);
                }
                else
                {
                    var items = ToModelItemCollection(group.Items);
                    updatedSet = new SelectionSet(items);
                }

                updatedSet.DisplayName = setName;
                doc.SelectionSets.ReplaceWithCopy(parent, existingIndex, updatedSet);

                result.Reused = true;
                result.Message = "Updated existing set.";
                return result;
            }

            if (existing is GroupItem)
            {
                result.Skipped = true;
                result.Message = "A folder with this value name already exists.";
                return result;
            }

            // Create brand new set via InsertCopy
            SelectionSet newSet;
            if (mode == AttributeSetMode.SearchSet)
            {
                var search = CreateSearch(doc, category, property, group.Value, group.Samples);
                newSet = new SelectionSet(search);
            }
            else
            {
                var items = ToModelItemCollection(group.Items);
                newSet = new SelectionSet(items);
            }

            newSet.DisplayName = setName;
            doc.SelectionSets.InsertCopy(parent, parent.Children.Count, newSet);

            result.Created = true;
            result.Message = "Created.";
            return result;
        }

        private static Search CreateSearch(
            Document doc,
            string category,
            string property,
            string value,
            IEnumerable<VariantData> samples)
        {
            var search = new Search
            {
                Locations = SearchLocations.DescendantsAndSelf,
                PruneBelowMatch = true
            };

            search.SearchConditions.Add(CreateCondition(category, property, value, samples, true));

            if (search.FindFirst(doc, false) == null)
            {
                search.Clear();
                search.SearchConditions.Add(CreateCondition(category, property, value, samples, false));
            }

            return search;
        }

        private static SearchCondition CreateCondition(
            string category,
            string property,
            string value,
            IEnumerable<VariantData> samples,
            bool useDisplayName)
        {
            var condition = useDisplayName
                ? SearchCondition.HasPropertyByDisplayName(category, property)
                : SearchCondition.HasPropertyByName(category, property);

            var variant = ToVariantData(value, samples);
            condition = condition.EqualValue(variant);

            if (variant.IsDisplayString)
                condition = condition.IgnoreStringValueCase();

            return condition;
        }

        private static VariantData ToVariantData(string value, IEnumerable<VariantData> samples)
        {
            var sampleTypes = (samples ?? Enumerable.Empty<VariantData>())
                .Where(sample => sample != null)
                .Select(sample => sample.DataType)
                .Distinct()
                .ToList();

            if (sampleTypes.Count == 1)
            {
                var type = sampleTypes[0];
                if (TryConvertByType(value, type, out var typed))
                    return typed;
            }

            if (sampleTypes.Contains(VariantDataType.Boolean) && bool.TryParse(value, out var boolValue))
                return VariantData.FromBoolean(boolValue);

            if (sampleTypes.Any(type => IsDoubleType(type)) && TryParseDouble(value, out var doubleValue))
                return VariantData.FromDouble(doubleValue);

            if (sampleTypes.Contains(VariantDataType.Int32) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                return VariantData.FromInt32(intValue);

            return VariantData.FromDisplayString(value ?? string.Empty);
        }

        private static bool TryConvertByType(string value, VariantDataType type, out VariantData result)
        {
            result = null;

            switch (type)
            {
                case VariantDataType.Boolean:
                    if (bool.TryParse(value, out var boolValue))
                    {
                        result = VariantData.FromBoolean(boolValue);
                        return true;
                    }
                    break;

                case VariantDataType.Int32:
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                    {
                        result = VariantData.FromInt32(intValue);
                        return true;
                    }
                    break;

                case VariantDataType.Double:
                    if (TryParseDouble(value, out var doubleValue))
                    {
                        result = VariantData.FromDouble(doubleValue);
                        return true;
                    }
                    break;

                case VariantDataType.DoubleLength:
                    if (TryParseDouble(value, out var lengthValue))
                    {
                        result = VariantData.FromDoubleLength(lengthValue);
                        return true;
                    }
                    break;

                case VariantDataType.DoubleArea:
                    if (TryParseDouble(value, out var areaValue))
                    {
                        result = VariantData.FromDoubleArea(areaValue);
                        return true;
                    }
                    break;

                case VariantDataType.DoubleVolume:
                    if (TryParseDouble(value, out var volumeValue))
                    {
                        result = VariantData.FromDoubleVolume(volumeValue);
                        return true;
                    }
                    break;

                case VariantDataType.DoubleAngle:
                    if (TryParseDouble(value, out var angleValue))
                    {
                        result = VariantData.FromDoubleAngle(angleValue);
                        return true;
                    }
                    break;

                case VariantDataType.IdentifierString:
                    result = VariantData.FromIdentifierString(value ?? string.Empty);
                    return true;

                case VariantDataType.DisplayString:
                    result = VariantData.FromDisplayString(value ?? string.Empty);
                    return true;

                case VariantDataType.DateTime:
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue))
                    {
                        result = VariantData.FromDateTime(dateValue);
                        return true;
                    }
                    break;
            }

            return false;
        }

        private static bool IsDoubleType(VariantDataType type)
        {
            return type == VariantDataType.Double ||
                   type == VariantDataType.DoubleLength ||
                   type == VariantDataType.DoubleArea ||
                   type == VariantDataType.DoubleVolume ||
                   type == VariantDataType.DoubleAngle;
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result) ||
                   double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out result);
        }

        private static FolderItem GetOrCreateFolder(Document doc, GroupItem parent, string name)
        {
            var existing = FindChild(parent, name);
            if (existing is FolderItem existingFolder)
            {
                // Folder already exists, reuse it
                return existingFolder;
            }

            if (existing is GroupItem)
                throw new InvalidOperationException($"Cannot create folder because another item already uses this name: {name}");

            var folder = new FolderItem
            {
                DisplayName = name
            };

            // Use InsertCopy to add the folder through the Navisworks API.
            // Direct mutation (parent.Children.Add) throws "read-only".
            doc.SelectionSets.InsertCopy(parent, parent.Children.Count, folder);

            // After InsertCopy the document tree is updated.
            // Re-find the parent in the live tree, then retrieve the inserted folder.
            GroupItem liveParent;
            if (parent == doc.SelectionSets.RootItem ||
                string.IsNullOrEmpty(parent.DisplayName))
            {
                // Parent is the root item
                liveParent = doc.SelectionSets.RootItem;
            }
            else
            {
                // Find parent by walking the live tree
                liveParent = FindGroupItemRecursive(doc.SelectionSets.RootItem, parent.DisplayName) ?? parent;
            }

            var liveFolder = FindChild(liveParent, name) as FolderItem;
            if (liveFolder != null)
                return liveFolder;

            // Ultimate fallback: search the entire tree
            return FindFolderRecursive(doc.SelectionSets.RootItem, name) ?? folder;
        }

        private static SavedItem FindChild(GroupItem parent, string name)
        {
            foreach (SavedItem child in parent.Children)
            {
                if (string.Equals(child.DisplayName?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return child;
            }

            return null;
        }

        private static int FindChildIndex(GroupItem parent, string name)
        {
            var index = 0;
            foreach (SavedItem child in parent.Children)
            {
                if (string.Equals(child.DisplayName?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return index;
                index++;
            }

            return -1;
        }

        private static FolderItem FindFolderRecursive(GroupItem parent, string name)
        {
            foreach (SavedItem child in parent.Children)
            {
                if (child is FolderItem folder)
                {
                    if (string.Equals(folder.DisplayName?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
                        return folder;

                    var nested = FindFolderRecursive(folder, name);
                    if (nested != null)
                        return nested;
                }
            }

            return null;
        }




        private static GroupItem FindGroupItemRecursive(GroupItem parent, string name)
        {
            foreach (SavedItem child in parent.Children)
            {
                if (child is GroupItem group)
                {
                    if (string.Equals(group.DisplayName?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
                        return group;

                    var nested = FindGroupItemRecursive(group, name);
                    if (nested != null)
                        return nested;
                }
            }

            return null;
        }

        private static ModelItemCollection ToModelItemCollection(IEnumerable<ModelItem> items)
        {
            var collection = new ModelItemCollection();
            foreach (var item in items)
                collection.Add(item);

            return collection;
        }

        private static bool TryGetProperty(
            ModelItem item,
            string category,
            string property,
            out DataProperty navisProperty)
        {
            navisProperty = null;

            try
            {
                if (item.PropertyCategories == null)
                    return false;

                foreach (PropertyCategory itemCategory in item.PropertyCategories)
                {
                    if (!Matches(GetDisplayName(itemCategory), category))
                        continue;

                    foreach (DataProperty itemProperty in itemCategory.Properties)
                    {
                        if (Matches(GetDisplayName(itemProperty), property))
                        {
                            navisProperty = itemProperty;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static IEnumerable<ModelItem> EnumerateModelItems(Document doc)
        {
            if (doc?.Models == null)
                yield break;

            foreach (Model model in doc.Models)
            {
                if (model?.RootItem == null)
                    continue;

                foreach (ModelItem item in model.RootItem.DescendantsAndSelf)
                    yield return item;
            }
        }

        private static string GetDisplayName(PropertyCategory category)
        {
            return category?.DisplayName ?? category?.Name;
        }

        private static string GetDisplayName(DataProperty property)
        {
            return property?.DisplayName ?? property?.Name;
        }

        private static string FormatValue(VariantData value)
        {
            if (value == null)
                return null;

            switch (value.DataType)
            {
                case VariantDataType.Double:
                    return value.ToDouble().ToString("G", CultureInfo.InvariantCulture);
                case VariantDataType.DoubleLength:
                    return value.ToDoubleLength().ToString("G", CultureInfo.InvariantCulture);
                case VariantDataType.DoubleArea:
                    return value.ToDoubleArea().ToString("G", CultureInfo.InvariantCulture);
                case VariantDataType.DoubleVolume:
                    return value.ToDoubleVolume().ToString("G", CultureInfo.InvariantCulture);
                case VariantDataType.DoubleAngle:
                    return value.ToDoubleAngle().ToString("G", CultureInfo.InvariantCulture);
                case VariantDataType.Int32:
                    return value.ToInt32().ToString(CultureInfo.InvariantCulture);
                case VariantDataType.Boolean:
                    return value.ToBoolean() ? "True" : "False";
                case VariantDataType.DisplayString:
                    return value.ToDisplayString();
                case VariantDataType.IdentifierString:
                    return value.ToIdentifierString();
                case VariantDataType.DateTime:
                    return value.ToDateTime().ToString("u", CultureInfo.InvariantCulture);
                case VariantDataType.NamedConstant:
                    return value.ToNamedConstant()?.DisplayName;
                default:
                    return value.ToString();
            }
        }

        private static bool Matches(string actual, string expected)
        {
            return string.Equals((actual ?? string.Empty).Trim(), (expected ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanName(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private sealed class GroupedValue
        {
            public string Value { get; }
            public List<ModelItem> Items { get; }
            public List<VariantData> Samples { get; }

            public GroupedValue(string value)
            {
                Value = value;
                Items = new List<ModelItem>();
                Samples = new List<VariantData>();
            }
        }
    }
}
