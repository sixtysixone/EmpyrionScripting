﻿using Eleon.Modding;
using EmpyrionNetAPIDefinitions;
using EmpyrionScripting.CsHelper;
using EmpyrionScripting.DataWrapper;
using EmpyrionScripting.Interface;
using EmpyrionScripting.Internal.Interface;
using HandlebarsDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EmpyrionScripting.CustomHelpers
{
    [HandlebarHelpers]
    public static class ConveyorHelpers
    {
        private const int PlayerCoreType = 558;

        readonly static object moveLock = new object();
        public static Action<string, LogLevel> Log { get; set; }
        public static Func<IScriptRootData, IPlayfield, IStructure, VectorInt3, IDeviceLock> CreateDeviceLock { get; set; } = (R, P, S, V) => new DeviceLock(R, P, S, V);
        public static Func<IScriptRootData, IPlayfield, IStructure, VectorInt3, IDeviceLock> WeakCreateDeviceLock { get; set; } = (R, P, S, V) => new WeakDeviceLock(R, P, S, V);

        public class ItemMoveInfo : IItemMoveInfo
        {
            public static IList<IItemMoveInfo> Empty = Array.Empty<ItemMoveInfo>();
            public int Id { get; set; }
            public IEntityData SourceE { get; set; }
            public string Source { get; set; }
            public IEntityData DestinationE { get; set; }
            public string Destination { get; set; }
            public int Count { get; set; }
            public string Error { get; set; }
        }

        [HandlebarTag("islocked")]
        public static void IsLockedHelper(TextWriter output, object rootObject, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length != 2 && arguments.Length != 4) throw new HandlebarsException("{{islocked structure device|x y z}} helper must have two or four argument: (structure) (device)|(x) (y) (z)");

            var root        = rootObject as IScriptRootData;
            var structure   = arguments[0] as IStructureData;
            VectorInt3 position;

            if (arguments.Length == 2)
            {
                var block = arguments[1] as BlockData;
                position  = block?.Position ?? new VectorInt3();
            }
            else
            {
                int.TryParse(arguments[1].ToString(), out var x);
                int.TryParse(arguments[2].ToString(), out var y);
                int.TryParse(arguments[3].ToString(), out var z);

                position = new VectorInt3(x, y, z);
            }

            try
            {
                var isLocked = root.GetCurrentPlayfield().IsStructureDeviceLocked(structure.GetCurrent().Id, position);

                if (isLocked) options.Template(output, context as object);
                else          options.Inverse (output, context as object);
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{islocked}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        [HandlebarTag("lockdevice")]
        public static void LockDeviceHelper(TextWriter output, object rootObject, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length != 2 && arguments.Length != 4) throw new HandlebarsException("{{lockdevice structure device|x y z}} helper must have two or four argument: @root (structure) (device)|(x) (y) (z)");

            var root                = rootObject as IScriptRootData;
            var S                   = arguments[0] as IStructureData;
            VectorInt3 position;

            if(!root.IsElevatedScript) throw new HandlebarsException("{{lockdevice}} only allowed in elevated scripts");

            if (!root.DeviceLockAllowed)
            {
                Log($"LockDevice: NoLockAllowed({root.ScriptId}): {root.CycleCounter} % {EmpyrionScripting.Configuration.Current.DeviceLockOnlyAllowedEveryXCycles}", LogLevel.Debug);
                return;
            }

            if (arguments.Length == 2)
            {
                var block = arguments[1] as BlockData;
                position  = block?.Position ?? new VectorInt3();
            }
            else
            {
                int.TryParse(arguments[1].ToString(), out var x);
                int.TryParse(arguments[2].ToString(), out var y);
                int.TryParse(arguments[3].ToString(), out var z);

                position = new VectorInt3(x, y, z);
            }

            try
            {
                using (var locked = CreateDeviceLock(root, root.GetCurrentPlayfield(), S.E?.S.GetCurrent(), position))
                {
                    if (locked.Success) options.Template(output, context as object);
                    else                options.Inverse (output, context as object);
                }
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{lockdevice}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        [HandlebarTag("additems")]
        public static void AddItemsHelper(TextWriter output, object rootObject, dynamic context, object[] arguments)
        {
            if (arguments.Length != 3) throw new HandlebarsException("{{additems container itemid count}} helper must have three arguments: (container) (item) (count)");

            var root                = rootObject as IScriptRootData;
            var block               = arguments[0] as BlockData;
            int.TryParse(arguments[1].ToString(), out var itemid);
            int.TryParse(arguments[2].ToString(), out var count);

            if (!root.IsElevatedScript) throw new HandlebarsException("{{additems}} only allowed in elevated scripts");

            try
            {
                var container = block.Device as ContainerData;
                container.GetContainer().AddItems(itemid, count);
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{additems}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        [HandlebarTag("removeitems")]
        public static void RemoveItemsHelper(TextWriter output, object rootObject, dynamic context, object[] arguments)
        {
            if (arguments.Length != 3) throw new HandlebarsException("{{removeitems container itemid maxcount}} helper must have three arguments: (container) (item) (maxcount)");

            var root                = rootObject as IScriptRootData;
            var block               = arguments[0] as BlockData;
            int.TryParse(arguments[1].ToString(), out var itemid);
            int.TryParse(arguments[2].ToString(), out var maxcount);

            if (!root.IsElevatedScript) throw new HandlebarsException("{{removeitems}} only allowed in elevated scripts");

            try
            {
                var container = block.Device as ContainerData;
                container.GetContainer().RemoveItems(itemid, maxcount);
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{removeitems}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        [HandlebarTag("trashcontainer")]
        public static void TrashContainerHelper(TextWriter output, object rootObject, dynamic context, object[] arguments)
        {
            if (arguments.Length != 2) throw new HandlebarsException("{{trashcontainer structure containername}} helper must have two arguments: (structure) (containername)");

            var root            = rootObject as IScriptRootData;
            var structure       = arguments[0] as IStructureData;
            var containerName   = arguments[1] as string;

            try
            {
                var containerPos = structure.GetCurrent().GetDevicePositions(containerName).FirstOrDefault();
                var container    = structure.GetCurrent().GetDevice<IContainer>(containerName);

                if (container == null) throw new HandlebarsException("{{trashcontainer}} conatiner not found '" + containerName + "'");

                using var locked = WeakCreateDeviceLock(root, root.GetCurrentPlayfield(), structure.GetCurrent(), containerPos);
                if (!locked.Success)
                {
                    Log($"DeviceIsLocked:{structure.E.Name} -> {containerName}", LogLevel.Debug);
                    return;
                }

                container.SetContent(new List<ItemStack>());
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{trashcontainer}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        [HandlebarTag("move")]
        public static void ItemMoveHelper(TextWriter output, object rootObject, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length < 3 || arguments.Length > 4) throw new HandlebarsException("{{move item structure names [max]}} helper must have at least three argument: (item) (structure) (name;name*;*;name) [max count targets]");

            var root = rootObject as IScriptRootData;
            try
            {
                var item        = arguments[0] as ItemsData;
                var structure   = arguments[1] as IStructureData;
                var namesSearch = arguments[2] as string;

                int? maxLimit = arguments.Length > 3 && int.TryParse(arguments[3]?.ToString(), out int limit) ? limit : (int?)null;

                var moveInfos = Move(root, item, structure, namesSearch, maxLimit);

                moveInfos
                    .Where(M => M.Error != null)
                    .ForEach(M => output.Write($"{M.Id}:{M.Source}->{M.Destination}:{M.Error}"));

                if (moveInfos.Count == 0) options.Inverse (output, context as object);
                else                      moveInfos.ForEach(I => options.Template(output, I));
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{move}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        public static IList<IItemMoveInfo> Move(IScriptRootData root, IItemsData item, IStructureData structure, string namesSearch, int? maxLimit)
        {
            if (!root.DeviceLockAllowed)
            {
                Log($"Move: NoLockAllowed({root.ScriptId}): {root.CycleCounter} % {EmpyrionScripting.Configuration.Current.DeviceLockOnlyAllowedEveryXCycles}", LogLevel.Debug);
                return ItemMoveInfo.Empty;
            }

            if (root.TimeLimitReached)
            {
                Log($"Move: TimeLimitReached({root.ScriptId})", LogLevel.Debug);
                return ItemMoveInfo.Empty;
            }

            var uniqueNames = structure.AllCustomDeviceNames.GetUniqueNames(namesSearch);
            if(!uniqueNames.Any())
            {
                Log($"NoDevicesFound: {namesSearch}", LogLevel.Debug);
                return ItemMoveInfo.Empty;
            }

            var moveInfos = new List<IItemMoveInfo>();

            lock (moveLock) item.Source
                 .ForEach(S => {
                    using var locked = WeakCreateDeviceLock(root, root.GetCurrentPlayfield(), S.E?.S.GetCurrent(), S.Position);
                    if (!locked.Success)
                    {
                        Log($"DeviceIsLocked (Source): {S.Id} #{S.Count} => {S.CustomName}", LogLevel.Debug);
                        return;
                    }

                    var count = S.Count;
                    count -= S.Container.RemoveItems(S.Id, count);
                    Log($"Move(RemoveItems): {S.CustomName} {S.Id} #{S.Count}->{count}", LogLevel.Debug);

                    ItemMoveInfo currentMoveInfo = null;

                    if (count > 0) uniqueNames
                                    .Where(N => N != S.CustomName)
                                    .ForEach(N => {
                                        var startCount = count;
                                        count = MoveItem(root, S, N, structure, count, maxLimit);
                                        if(startCount != count){
                                            var movedCount = startCount - count;
                                            moveInfos.Add(currentMoveInfo = new ItemMoveInfo() {
                                                Id              = S.Id,
                                                Count           = movedCount,
                                                SourceE         = S.E,
                                                Source          = S.CustomName,
                                                DestinationE    = structure.E,
                                                Destination     = N,
                                            });

                                            Log($"Move(AddItems): {S.CustomName} {S.Id} #{S.Count}->{startCount - count}", LogLevel.Debug);

                                            // Für diesen Scriptdurchlauf dieses Item aus der Verarbeitung nehmen
                                            S.Count -= movedCount;
                                        };
                                    }, () => root.TimeLimitReached);

                     if (count > 0)
                     {
                         var retoureCount = count;
                         count = S.Container.AddItems(S.Id, retoureCount);
                         Log($"Move(retoure): {S.CustomName} {retoureCount} -> {count}", LogLevel.Debug);
                     }

                     if (count > 0)
                     {
                         root.GetPlayfieldScriptData().MoveLostItems.Enqueue(new ItemMoveInfo()
                         {
                             Id         = S.Id,
                             Count      = count,
                             SourceE    = S.E,
                             Source     = S.CustomName,
                         });
                        currentMoveInfo.Error = $"{{move}} error lost #{count} of item {S.Id} in container {S.CustomName} -> add to retry list";
                     }

                 }, () => root.TimeLimitReached);

            return moveInfos;
        }

        public static void HandleMoveLostItems(PlayfieldScriptData root)
        {
            var tryCounter = root.MoveLostItems.Count;

            while (tryCounter-- > 0 && root.MoveLostItems.TryDequeue(out var restore))
            {
                try
                {
                    var targetStructure = restore.SourceE.S.GetCurrent();
                    var targetPos       = targetStructure.GetDevicePositions(restore.Source).FirstOrDefault();
                    var targetContainer = targetStructure.GetDevice<IContainer>(restore.Source);

                    if(targetContainer == null)
                    {
                        Log($"HandleMoveLostItems(target container not found): {restore.Source} {restore.Id} #{restore.Count}", LogLevel.Message);
                        root.MoveLostItems.Enqueue(restore);
                        continue;
                    }

                    var isLocked = restore.SourceE.GetCurrentPlayfield().IsStructureDeviceLocked(restore.SourceE.S.GetCurrent().Id, targetPos);
                    if (isLocked)
                    {
                        Log($"HandleMoveLostItems(container is locked): {restore.Source} {restore.Id}", LogLevel.Debug);
                        root.MoveLostItems.Enqueue(restore);
                        continue;
                    }

                    var count = targetContainer.AddItems(restore.Id, restore.Count);
                    var stackSize = 0;

                    try
                    {
                        stackSize = (int)EmpyrionScripting.ConfigEcfAccess.FindAttribute(restore.Id, "StackSize");
                        if (stackSize < count)
                        {
                            Log($"HandleMoveLostItems(split invalid stacks): {restore.Source} {restore.Id} #{restore.Count}->{stackSize}", LogLevel.Message);

                            var countSplit = count;
                            while (countSplit > 0)
                            {
                                root.MoveLostItems.Enqueue(new ItemMoveInfo()
                                {
                                    Id      = restore.Id,
                                    Count   = Math.Min(countSplit, stackSize),
                                    SourceE = restore.SourceE,
                                    Source  = restore.Source,
                                });
                                countSplit -= stackSize;
                            }

                            continue;
                        }
                    }
                    catch { /* Fehler ignorieren */ }

                    // AddItem funktioniert leider nicht (mehr) wenn den der Stack gar nicht in den Container passt
                    if (count > 0 && count == restore.Count && count > stackSize)
                    {
                        var content = targetContainer.GetContent();

                        if (content.Count < 64)
                        {
                            content.Add(new ItemStack(restore.Id, restore.Count));
                            targetContainer.SetContent(content);
                            Log($"HandleMoveLostItems(restored set content fallback): {restore.Source} {restore.Id} #{restore.Count}", LogLevel.Message);

                            continue;
                        }
                    }

                    if (count > 0)
                    {
                        root.MoveLostItems.Enqueue(new ItemMoveInfo()
                        {
                            Id      = restore.Id,
                            Count   = count,
                            SourceE = restore.SourceE,
                            Source  = restore.Source,
                        });
                        Log($"HandleMoveLostItems(partial restored): {restore.Source} {restore.Id} #{restore.Count} -> {count}", restore.Count == count ? LogLevel.Debug : LogLevel.Message);
                    }
                    else Log($"HandleMoveLostItems(restored): {restore.Source} {restore.Id} #{restore.Count}", LogLevel.Message);
                }
                catch (Exception error)
                {
                    Log($"HandleMoveLostItems(error): {restore.Source} {restore.Id} #{restore.Count} -> {EmpyrionScripting.ErrorFilter(error)}", LogLevel.Message);
                    root.MoveLostItems.Enqueue(restore);
                }

                if (root.ScriptExecQueue.TimeLimitSyncReached()) break;
            }
        }

        private static int MoveItem(IScriptRootData root, IItemsSource S, string N, IStructureData targetStructure, int count, int? maxLimit)
        {
            var target = targetStructure?.GetCurrent()?.GetDevice<Eleon.Modding.IContainer>(N);
            if (target == null)
            {
                Log($"TargetNoFound: {S.Id} #{S.Count} => {N}", LogLevel.Debug);
                return count;
            }

            if (!targetStructure.ContainerSource.TryGetValue(N, out var targetData))
            {
                Log($"TargetDataNoFound: {S.Id} #{S.Count} => {N}", LogLevel.Debug);
                return count;
            }

            var tryMoveCount = maxLimit.HasValue
                ? Math.Max(0, Math.Min(count, maxLimit.Value - target.GetTotalItems(S.Id)))
                : count;

            using var locked = WeakCreateDeviceLock(root, root.GetCurrentPlayfield(), targetStructure.GetCurrent(), targetData.Position);
            if (!locked.Success)
            {
                Log($"DeviceIsLocked (Target): {S.Id} #{S.Count} => {targetData.CustomName}", LogLevel.Debug);
                return count;
            }

            return maxLimit.HasValue
                ? target.AddItems(S.Id, tryMoveCount) + (count - tryMoveCount)
                : target.AddItems(S.Id, tryMoveCount);
        }

        [HandlebarTag("fill")]
        public static void FillHelper(TextWriter output, object rootObject, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length < 3 || arguments.Length > 4) throw new HandlebarsException("{{fill item structure tank [max]}} helper must have at least three argument: (item) (structure) (tank) [max count/percentage targets]");

            var root = rootObject as IScriptRootData;
            try
            {
                if (!(arguments[1] is IStructureData structure) || !(arguments[0] is ItemsData item)) return;

                if (!Enum.TryParse<StructureTankType>(arguments[2]?.ToString(), true, out var type))
                {
                    output.WriteLine($"unknown type {arguments[2]}");
                    return;
                }

                int maxLimit = arguments.Length > 3 && int.TryParse(arguments[3]?.ToString(), out int limit) ? Math.Min(100, Math.Max(0, limit)) : 100;

                var moveInfos = Fill(root, item, structure, type, maxLimit);

                moveInfos
                    .Where(M => M.Error != null)
                    .ForEach(E => output.Write(E), () => !root.Running);

                if (moveInfos.Count == 0) options.Inverse(output, context as object);
                else                      moveInfos.ForEach(I => options.Template(output, I), () => root.TimeLimitReached);
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{fill}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        public static IList<IItemMoveInfo> Fill(IScriptRootData root, IItemsData item, IStructureData structure, StructureTankType type, int maxLimit)
        {
            if (!root.DeviceLockAllowed)
            {
                Log($"Fill: NoLockAllowed({root.ScriptId}): {root.CycleCounter} % {EmpyrionScripting.Configuration.Current.DeviceLockOnlyAllowedEveryXCycles}", LogLevel.Debug);
                return ItemMoveInfo.Empty;
            }

            var specialTransfer = type switch
            {
                StructureTankType.Oxygen    => structure.OxygenTank  ,
                StructureTankType.Fuel      => structure.FuelTank    ,
                StructureTankType.Pentaxid  => structure.PentaxidTank,
                _                           => null,
            };

            if (specialTransfer == null || !specialTransfer.AllowedItem(item.Id)) return ItemMoveInfo.Empty;

            Log($"Fill Total: #{item.Source.Count}", LogLevel.Debug);

            var moveInfos = new List<IItemMoveInfo>();

            lock(moveLock) item.Source
                .ForEach(S => {
                    using var locked = WeakCreateDeviceLock(root, root.GetCurrentPlayfield(), S.E?.S.GetCurrent(), S.Position);
                    if (!locked.Success)
                    {
                        Log($"DeviceIsLocked (Source): {S.Id} #{S.Count} => {S.CustomName}", LogLevel.Debug);
                        return;
                    }

                    var count = specialTransfer.ItemsNeededForFill(S.Id, maxLimit);
                    if (count > 0)
                    {
                        count -= S.Container.RemoveItems(S.Id, count);
                        Log($"Move(RemoveItems): {S.CustomName} {S.Id} #{S.Count}->{count}", LogLevel.Debug);
                    }

                    ItemMoveInfo currentMoveInfo = null;

                    if (count > 0)
                    {
                        var startCount = count;
                        count = specialTransfer.AddItems(S.Id, count);
                        if (startCount != count) moveInfos.Add(currentMoveInfo = new ItemMoveInfo()
                        {
                            Id = S.Id,
                            Count = startCount - count,
                            SourceE = S.E,
                            Source = S.CustomName,
                            DestinationE = structure.E,
                            Destination = type.ToString(),
                        });
                    };

                    if (count > 0) count = S.Container.AddItems(S.Id, count);
                    if (count > 0 && currentMoveInfo != null)
                    {
                        root.GetPlayfieldScriptData().MoveLostItems.Enqueue(new ItemMoveInfo()
                        {
                            Id         = S.Id,
                            Count      = count,
                            SourceE    = S.E,
                            Source     = S.CustomName,
                        });
                        currentMoveInfo.Error = $"{{fill}} error lost #{count} of item {S.Id} in container {S.CustomName} -> add to retry list";
                    }

                }, () => root.TimeLimitReached);

            return moveInfos;
        }

        public class ProcessBlockData
        {
            public DateTime Started { get; set; }
            public DateTime Finished { get; set; }
            public string Name { get; set; }
            public int Id { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
            public int TotalBlocks { get; set; }
            public int CheckedBlocks { get; set; }
            public int RemovedBlocks { get; set; }
            public VectorInt3 MinPos { get; set; }
            public VectorInt3 MaxPos { get; set; }
        }

        [HandlebarTag("deconstruct")]
        public static void DeconstructHelper(TextWriter output, object rootObject, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length < 2 || arguments.Length > 4) throw new HandlebarsException("{{deconstruct entity container [CorePrefix] [RemoveItemsIds1,Id2,...]}} helper must have two to four argument: entity (container;container*;*;container) [CorePrefix] [RemoveItemsIds]");

            var root = rootObject as IScriptRootData;
            var E    = arguments[0] as IEntityData;
            var N    = arguments[1]?.ToString();

            try
            {
                var list = arguments.Get(3)?.ToString()
                    .Split(new[] { ',', ';' })
                    .Select(T => T.Trim())
                    .Select(T => {
                        var delimiter = T.IndexOf('-', 1);
                        return delimiter > 0
                            ? new Tuple<int, int>(int.TryParse(T.Substring(0, delimiter), out var l1) ? l1 : 0, int.TryParse(T.Substring(delimiter + 1), out var r1) ? r1 : 0)
                            : new Tuple<int, int>(int.TryParse(T, out var l2) ? l2 : 0, int.TryParse(T, out var r2) ? r2 : 0);
                    })
                    .ToArray();

                ConvertBlocks(output, root, options, context as object, arguments,
                    (arguments.Get(2)?.ToString() ?? "Core-Destruct") + $"-{E.Id}", list,
                    DeconstructBlock);
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{deconstruct}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        [HandlebarTag("recycle")]
        public static void RecycleHelper(TextWriter output, object rootObject, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length < 2 || arguments.Length > 3) throw new HandlebarsException("{{recycle entity container [CorePrefix]}} helper must have two to four argument: entity (container;container*;*;container) [CorePrefix] [RemoveItemsIds]");

            var root = rootObject   as IScriptRootData;
            var E    = arguments[0] as IEntityData;

            try
            {
                ConvertBlocks(output, root, options, context as object, arguments, 
                    (arguments.Get(2)?.ToString() ?? "Core-Recycle") + $"-{E.Id}", null,
                    ExtractBlockToRecipe);
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{deconstruct}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }

        public static void ConvertBlocks(TextWriter output, IScriptRootData root, HelperOptions options, object context, object[] arguments, string coreName,
            Tuple<int, int>[] list, Func<IEntityData, Dictionary<int, double>, int, bool> processBlock)
        { 
            var E    = arguments[0] as IEntityData;
            var N    = arguments[1]?.ToString();

            var minPos      = E.S.GetCurrent().MinPos;
            var maxPos      = E.S.GetCurrent().MaxPos;
            var S           = E.S.GetCurrent();
            var corePosList = E.S.GetCurrent().GetDevicePositions(coreName);
            var directId    = root.IsElevatedScript ? (int.TryParse(arguments.Get(2)?.ToString(), out var manualId) ? manualId : 0) : 0;

            var uniqueNames = root.E.S.AllCustomDeviceNames.GetUniqueNames(N).ToList();

            if (!uniqueNames.Any())
            {
                root.GetPersistendData().TryRemove(root.ScriptId, out _);
                options.Inverse(output, context);
                output.WriteLine($"No target container '{N}' found");
                return;
            }

            IContainer target    = null;
            VectorInt3 targetPos = VectorInt3.Undef;

            var firstTarget = GetNextContainer(root, uniqueNames, ref target, ref targetPos);
            if(firstTarget == null)
            {
                root.GetPersistendData().TryRemove(root.ScriptId, out _);
                options.Inverse(output, context);
                output.WriteLine($"Containers '{N}' are locked");
                return;
            }

            EmpyrionScripting.Log($"Ressource to first conatiner: {firstTarget}", LogLevel.Message);

            if (directId != E.Id)
            {
                if (corePosList.Count == 0)
                {
                    root.GetPersistendData().TryRemove(root.ScriptId, out _);
                    options.Inverse(output, context);
                    output.WriteLine($"No core '{coreName}' found on {E.Id}");
                    return;
                }

                var corePos = corePosList.First();
                var core = E.S.GetCurrent().GetBlock(corePos);
                core.Get(out var coreBlockType, out _, out _, out _);

                if (coreBlockType != PlayerCoreType)
                {
                    root.GetPersistendData().TryRemove(root.ScriptId, out _);
                    options.Inverse(output, context);
                    output.WriteLine($"No core '{coreName}' found on {E.Id} wrong type {coreBlockType}");
                    return;
                }
            }

            var processBlockData = root.GetPersistendData().GetOrAdd(root.ScriptId + E.Id, K => new ProcessBlockData() {
                Started     = DateTime.Now,
                Name        = E.Name,
                Id          = E.Id,
                MinPos      = minPos,
                MaxPos      = maxPos,
                X           = minPos.x,
                Y           = maxPos.y,
                Z           = minPos.z,
                TotalBlocks =   (Math.Abs(minPos.x) + Math.Abs(maxPos.x) + 1) *
                                (Math.Abs(minPos.y) + Math.Abs(maxPos.y) + 1) *
                                (Math.Abs(minPos.z) + Math.Abs(maxPos.z) + 1)
            }) as ProcessBlockData;

            if(processBlockData.CheckedBlocks < processBlockData.TotalBlocks){
                var ressources = new Dictionary<int, double>();

                lock(processBlockData) ProcessBlockPart(output, root, S, processBlockData, target, targetPos, N, 0, list, (c, i) => processBlock(E, ressources, i));

                var allToLostItemRecover = false;
                var currentContainer = firstTarget;

                var ressourcesWithStackLimit = new List<KeyValuePair<int, double>>();

                ressources.ForEach(r =>
                    {
                        try
                        {
                            var stackSize = (int)EmpyrionScripting.ConfigEcfAccess.FindAttribute(r.Key, "StackSize");
                            var count = (int)r.Value;
                            while(count > 0)
                            {
                                ressourcesWithStackLimit.Add(new KeyValuePair<int, double>(r.Key, Math.Min(count, stackSize)));
                                count -= stackSize;
                            }
                        }
                        catch
                        {
                            ressourcesWithStackLimit.Add(r);
                        }
                    }
                );

                ressourcesWithStackLimit.ForEach(R =>
                {
                    var over = allToLostItemRecover ? (int)R.Value : target.AddItems(R.Key, (int)R.Value);

                    if (over > 0 && !allToLostItemRecover)
                    {
                        EmpyrionScripting.Log($"Container full: {R.Key} #{over} -> {currentContainer}", LogLevel.Message);

                        currentContainer = GetNextContainer(root, uniqueNames, ref target, ref targetPos);
                        if (currentContainer != null)
                        {
                            var nextTry = over;
                            over = target.AddItems(R.Key, over);
                            EmpyrionScripting.Log($"Ressource to NextContainer: {R.Key} #{nextTry} -> #{over} -> {currentContainer}", LogLevel.Message);
                        }
                        else
                        {
                            EmpyrionScripting.Log("All Container full or blocked", LogLevel.Message);
                            allToLostItemRecover = true;
                        }
                    }

                    if (over > 0)
                    {
                        EmpyrionScripting.Log($"Ressource to LostItemsRecover: {R.Key} #{over} -> {firstTarget}", LogLevel.Message);

                        root.GetPlayfieldScriptData().MoveLostItems.Enqueue(new ItemMoveInfo()
                        {
                            Id      = R.Key,
                            Count   = over,
                            SourceE = root.E,
                            Source  = firstTarget,
                        });
                    }
                });

                if(processBlockData.CheckedBlocks == processBlockData.TotalBlocks) processBlockData.Finished = DateTime.Now;
            }
            else if((DateTime.Now - processBlockData.Finished).TotalMinutes > 1) root.GetPersistendData().TryRemove(root.ScriptId + E.Id, out _);

            options.Template(output, processBlockData);
        }

        private static string GetNextContainer(IScriptRootData root, List<string> uniqueNames, ref IContainer target, ref VectorInt3 targetPos)
        {
            while (uniqueNames.Any())
            {
                var currentTarget = uniqueNames.First();
                uniqueNames.RemoveAt(0);

                try
                {
                    target    = root.E.S.GetCurrent().GetDevice<Eleon.Modding.IContainer>(currentTarget);
                    targetPos = root.E.S.GetCurrent().GetDevicePositions(currentTarget).First();

                    if(target != null)
                    {
                        var locking = WeakCreateDeviceLock(root, root.GetCurrentPlayfield(), root.E.S.GetCurrent(), targetPos);

                        if (locking.Exit)
                        {
                            //EmpyrionScripting.Log($"GetNextContainer:{currentTarget} at pos {targetPos} -> Exit", LogLevel.Debug);
                            return null;
                        }

                        if (locking.Success)
                        {
                            EmpyrionScripting.Log($"GetNextContainer:{currentTarget} at pos {targetPos}", LogLevel.Debug);
                            return currentTarget;
                        }
                        else
                        {
                            EmpyrionScripting.Log($"GetNextContainer: {currentTarget} at pos {targetPos} -> no free container", LogLevel.Debug);
                        }
                    }
                }
                catch (Exception error)
                {
                    EmpyrionScripting.Log($"GetNextContainer: {currentTarget} at pos {targetPos} -> no container {error}", LogLevel.Debug);
                }
            }

            return null;
        }

        [HandlebarTag("replaceblocks")]
        public static void ReplaceBlocksHelper(TextWriter output, object rootObject, HelperOptions options, dynamic context, object[] arguments)
        {
            if (arguments.Length != 3) throw new HandlebarsException("{{replaceblocks entity RemoveItemsIds1,Id2,... ReplaceId}} helper must have tree argument: entity RemoveItemsIds ReplaceId");

            var root = rootObject as IScriptRootData;
            var E    = arguments[0] as IEntityData;

            try
            {
                if(!root.IsElevatedScript) throw new HandlebarsException("only allowed in elevated scripts");

                var minPos      = E.S.GetCurrent().MinPos;
                var maxPos      = E.S.GetCurrent().MaxPos;
                var S           = E.S.GetCurrent();
                var list        = arguments.Get(1)?.ToString()
                                    .Split(new []{ ',', ';' })
                                    .Select(T => T.Trim())
                                    .Select(T => { 
                                        var delimiter = T.IndexOf('-', 1); 
                                        return delimiter > 0 
                                            ? new Tuple<int,int>(int.TryParse(T.Substring(0, delimiter), out var l1) ? l1 : 0, int.TryParse(T.Substring(delimiter + 1), out var r1) ? r1 : 0)
                                            : new Tuple<int,int>(int.TryParse(T, out var l2) ? l2 : 0, int.TryParse(T, out var r2) ? r2 : 0); 
                                    })
                                    .ToArray();
                int.TryParse(arguments.Get(2)?.ToString(), out var replaceId);

                var processBlockData = root.GetPersistendData().GetOrAdd(root.ScriptId + E.Id, K => new ProcessBlockData() {
                    Started     = DateTime.Now,
                    Name        = E.Name,
                    Id          = E.Id,
                    MinPos      = minPos,
                    MaxPos      = maxPos,
                    X           = minPos.x,
                    Y           = maxPos.y,
                    Z           = minPos.z,
                    TotalBlocks =   (Math.Abs(minPos.x) + Math.Abs(maxPos.x) + 1) *
                                    (Math.Abs(minPos.y) + Math.Abs(maxPos.y) + 1) *
                                    (Math.Abs(minPos.z) + Math.Abs(maxPos.z) + 1)
                }) as ProcessBlockData;

                if(processBlockData.CheckedBlocks < processBlockData.TotalBlocks){
                    lock(processBlockData) ProcessBlockPart(output, root, S, processBlockData, null, VectorInt3.Undef, null, replaceId, list, (C, I) => C.AddItems(I, 1) > 0);
                    if(processBlockData.CheckedBlocks == processBlockData.TotalBlocks) processBlockData.Finished = DateTime.Now;
                }
                else if((DateTime.Now - processBlockData.Finished).TotalMinutes > 1) root.GetPersistendData().TryRemove(root.ScriptId + E.Id, out _);

                options.Template(output, processBlockData);
            }
            catch (Exception error)
            {
                if (!CsScriptFunctions.FunctionNeedsMainThread(error, root)) output.Write("{{replaceblocks}} error " + EmpyrionScripting.ErrorFilter(error));
            }
        }


        static void ProcessBlockPart(TextWriter output, IScriptRootData root, IStructure S, ProcessBlockData processBlockData, 
            IContainer target, VectorInt3 targetPos, string N, int replaceId, Tuple<int,int>[] list,
            Func<IContainer, int, bool> processBlock)
        {
            IDeviceLock locked = null;

            try
            {
                for (; processBlockData.Y >= processBlockData.MinPos.y; processBlockData.Y--)
                {
                    for (; processBlockData.X <= processBlockData.MaxPos.x; processBlockData.X++)
                    {
                        for (; processBlockData.Z <= processBlockData.MaxPos.z; processBlockData.Z++)
                        {
                            processBlockData.CheckedBlocks++;

                            var block = S.GetBlock(processBlockData.X, 128 + processBlockData.Y, processBlockData.Z);
                            if (block != null)
                            {
                                block.Get(out var blockType, out _, out _, out _);

                                if(list != null     && 
                                   list.Length > 0  && 
                                  !list.Any(L => L.Item1 <= blockType && L.Item2 >= blockType)) blockType = 0;

                                if (blockType > 0 && blockType != PlayerCoreType)
                                {
                                    if (EmpyrionScripting.Configuration.Current?.DeconstructBlockSubstitution != null &&
                                        EmpyrionScripting.Configuration.Current.DeconstructBlockSubstitution.TryGetValue(blockType, out var substituteTo)) blockType = substituteTo;

                                    if (blockType > 0 && N != null)
                                    {
                                        locked = locked ?? WeakCreateDeviceLock(root, root.GetCurrentPlayfield(), root.E.S.GetCurrent(), targetPos);
                                        if (!locked.Success)
                                        {
                                            processBlockData.CheckedBlocks--;
                                            output.WriteLine($"Container '{N}' is locked");
                                            return;
                                        }

                                        if (processBlock(target, blockType))
                                        {
                                            processBlockData.CheckedBlocks--;
                                            output.WriteLine($"Container '{N}' is full");
                                            return;
                                        }
                                    }

                                    block.Set(replaceId);
                                    processBlockData.RemovedBlocks++;

                                    if (processBlockData.RemovedBlocks > 100 && processBlockData.RemovedBlocks % 100 == 0 && root.TimeLimitReached) return;
                                }
                            }
                        }
                        processBlockData.Z = processBlockData.MinPos.z;
                    }
                    processBlockData.X = processBlockData.MinPos.x;
                }
            }
            finally
            {
                locked?.Dispose();
            }
        }

        private static bool DeconstructBlock(IEntityData E, Dictionary<int, double> ressources, int blockId)
        {
            string blockName = EmpyrionScripting.ConfigEcfAccess.FindAttribute(blockId, "PickupTarget")?.ToString();

            if (string.IsNullOrEmpty(blockName) && EmpyrionScripting.ConfigEcfAccess.FlatConfigBlockById.TryGetValue(blockId, out var blockData))
            {
                blockName = blockData.Values.TryGetValue("Name", out var name) ? name.ToString() : null;
            }

            if (!string.IsNullOrEmpty(blockName) && EmpyrionScripting.ConfigEcfAccess.ParentBlockName.TryGetValue(PlaceAtType(E.EntityType) + blockName, out var parentBlockName1)) blockName = parentBlockName1;
            if (!string.IsNullOrEmpty(blockName) && EmpyrionScripting.ConfigEcfAccess.ParentBlockName.TryGetValue(                            blockName, out var parentBlockName2)) blockName = parentBlockName2;

            var id = blockName != null && EmpyrionScripting.ConfigEcfAccess.BlockIdMapping.TryGetValue(blockName, out var mappedBlockId) ? mappedBlockId : blockId;

            if (ressources.TryGetValue(id, out var count)) ressources[id] = count + 1;
            else                                           ressources.Add(id, 1);

            return false;
        }

        private static bool ExtractBlockToRecipe(IEntityData E, Dictionary<int, double> ressources, int blockId)
        {
            EmpyrionScripting.ConfigEcfAccess.FlatConfigBlockById.TryGetValue(blockId, out var blockData);

            if (!EmpyrionScripting.ConfigEcfAccess.ResourcesForBlockById.TryGetValue(blockId, out var recipe))
            {
                if(blockData?.Values != null && blockData.Values.ContainsKey("Name")){
                    string parentBlockName = null;
                    if (EmpyrionScripting.ConfigEcfAccess.ParentBlockName.TryGetValue(PlaceAtType(E.EntityType) + blockData.Values["Name"].ToString(), out var parentBlockName1)) parentBlockName = parentBlockName1;
                    if (EmpyrionScripting.ConfigEcfAccess.ParentBlockName.TryGetValue(                            blockData.Values["Name"].ToString(), out var parentBlockName2)) parentBlockName = parentBlockName2;

                    if (parentBlockName != null && EmpyrionScripting.ConfigEcfAccess.ResourcesForBlockById.TryGetValue(EmpyrionScripting.ConfigEcfAccess.BlockIdMapping[parentBlockName], out var parentRecipe)) recipe = parentRecipe;
                }

                if (recipe == null)
                {
                    EmpyrionScripting.Log($"No recipe for {blockId}:{(EmpyrionScripting.ConfigEcfAccess.FlatConfigBlockById.TryGetValue(blockId, out var noRecipeBlock) ? noRecipeBlock.Values["Name"] : "")}", LogLevel.Message);
                    return false;
                }
            }
            EmpyrionScripting.Log($"Recipe for [{blockId}] {blockData?.Values["Name"]}: {recipe.Aggregate("", (r, i) => $"{r}\n{i.Key}:{i.Value}")}", LogLevel.Debug);

            recipe.ForEach(R =>
            {
                if (ressources.TryGetValue(R.Key, out var count)) ressources[R.Key] = count + R.Value;
                else                                              ressources.Add(R.Key, R.Value);
            });

            return false;
        }

        private static string PlaceAtType(EntityType entityType) => entityType switch
        {
            EntityType.BA => "Base",
            EntityType.CV => "MS",
            EntityType.SV => "SS",
            EntityType.HV => "GV",
            _             => string.Empty,
        };
    }
}
