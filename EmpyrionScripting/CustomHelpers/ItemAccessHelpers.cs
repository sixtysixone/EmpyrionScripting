﻿using EmpyrionScripting.DataWrapper;
using HandlebarsDotNet;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace EmpyrionScripting.CustomHelpers
{
    [HandlebarHelpers]
    public class ItemAccessHelpers
    {
        [HandlebarTag("items")]
        public static void ItemsBlockHelper(TextWriter output, object root, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length != 2) throw new HandlebarsException("{{items structure names}} helper must have exactly two argument: (structure) (name;name*;*;name)");

            var structure   = arguments[0] as IStructureData;
            var namesSearch = arguments[1]?.ToString();

            try
            {
                var uniqueNames = structure.AllCustomDeviceNames.GetUniqueNames(namesSearch);

                var allItems = new ConcurrentDictionary<int, ItemsData>();
                structure.Items
                    .SelectMany(I => I.Source.Where(S => S.CustomName != null && uniqueNames.Contains(S.CustomName)))
                    .ForEach(I =>
                    {
                        ItemInfo details = null;
                        EmpyrionScripting.ItemInfos?.ItemInfo.TryGetValue(I.Id, out details);
                        allItems.AddOrUpdate(I.Id,
                        new ItemsData()
                        {
                            Source  = new[] { I }.ToList(),
                            Id      = I.Id,
                            Count   = I.Count,
                            Key     = details == null ? I.Id.ToString() : details.Key,
                            Name    = details == null ? I.Id.ToString() : details.Name,
                        },
                        (K, U) => U.AddCount(I.Count, I));
                    });


                if (allItems.Count > 0) allItems.Values.OrderBy(I => I.Id).ForEach(I => options.Template(output, I));
                else                    options.Inverse(output, context as object);
            }
            catch (Exception error)
            {
                output.Write("{{items}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        [HandlebarTag("getitems")]
        public static void GetItemsBlockHelper(TextWriter output, object root, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length != 2) throw new HandlebarsException("{{getitems structure names}} helper must have exactly two argument: (structure) (name;name*;*;name)");

            var structure = arguments[0] as IStructureData;
            var namesSearch = arguments[1]?.ToString();

            try
            {
                var uniqueNames = structure.AllCustomDeviceNames.GetUniqueNames(namesSearch);

                var allItems = new ConcurrentDictionary<int, ItemsData>();
                structure.Items
                    .SelectMany(I => I.Source.Where(S => S.CustomName != null && uniqueNames.Contains(S.CustomName)))
                    .ForEach(I =>
                    {
                        ItemInfo details = null;
                        EmpyrionScripting.ItemInfos?.ItemInfo.TryGetValue(I.Id, out details);
                        allItems.AddOrUpdate(I.Id,
                        new ItemsData()
                        {
                            Source  = new[] { I }.ToList(),
                            Id      = I.Id,
                            Count   = I.Count,
                            Key     = details == null ? I.Id.ToString() : details.Key,
                            Name    = details == null ? I.Id.ToString() : details.Name,
                        },
                        (K, U) => U.AddCount(I.Count, I));
                    });


                if (allItems.Count > 0) options.Template(output, allItems.Values.OrderBy(I => I.Id).ToArray());
                else                    options.Inverse(output, context as object);
            }
            catch (Exception error)
            {
                output.Write("{{getitems}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        [HandlebarTag("itemlist")]
        public static void ItemListBlockHelper(TextWriter output, object root, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length != 2) throw new HandlebarsException("{{itemlist list ids}} helper must have exactly two argument: (list) (id1;id2;id3)");

            var items = arguments[0] as ItemsData[];
            var ids   = (arguments[1] as string)
                            .Split(';', ',')
                            .Select(N => int.TryParse(N, out int i) ? i : 0)
                            .Where(i => i != 0)
                            .ToArray();

            try
            {
                var list = items.ToDictionary(I => I.Id, I => I);
                ids.Where(i => !list.ContainsKey(i)).ForEach(i => {
                    EmpyrionScripting.ItemInfos.ItemInfo.TryGetValue(i, out ItemInfo details);
                    list.Add(i, new ItemsData()
                    {
                        Id    = i,
                        Count = 0,
                        Key   = details == null ? i.ToString() : details.Key,
                        Name  = details == null ? i.ToString() : details.Name,
                    });
                });

                if (list.Count > 0) list.Values
                                        .Where(i => ids.Contains(i.Id))
                                        .OrderBy(I => I.Id)
                                        .ForEach(I => options.Template(output, I));
                else                options.Inverse (output, context as object);
            }
            catch (Exception error)
            {
                output.Write("{{itemlist}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

    }
}
