﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eleon.Modding;
using EmpyrionScripting;
using EmpyrionScripting.CustomHelpers;
using EmpyrionScripting.DataWrapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace EmpyrionScripting.UnitTests
{
    [TestClass]
    public class UnitTestCs
    {
        [TestMethod]
        public void TestMethodCsSimpleTextReturn()
        {
            var lcdMod = new EmpyrionScripting();
            EmpyrionScripting.Configuration.Current.CsScriptsAllowed = CsScriptsAllowed.Everywhere;
            var pf = new PlayfieldScriptData(lcdMod);

            var dict = new ConcurrentDictionary<string, object>();

            var data = Substitute.For<IScriptRootData>();
            data.GetPersistendData().ReturnsForAnyArgs(dict);
            data.Data = dict;

            data.CycleCounter.Returns(0);
            Assert.AreEqual("3", lcdMod.ExecuteCsScript(pf, data, "return \"3\";"));
        }

        [TestMethod]
        public void TestMethodCsScriptWithException()
        {
            var lcdMod = new EmpyrionScripting();
            EmpyrionScripting.Configuration.Current.CsScriptsAllowed = CsScriptsAllowed.Everywhere;
            var pf = new PlayfieldScriptData(lcdMod);

            var dict = new ConcurrentDictionary<string, object>();

            var data = Substitute.For<IScriptRootData>();
            data.GetPersistendData().ReturnsForAnyArgs(dict);
            data.Data = dict;

            data.CycleCounter.Returns(0);
            Assert.AreEqual("Exception: Test", lcdMod.ExecuteCsScript(pf, data, "throw new Exception(\"Test\");"));
        }

        [TestMethod]
        public void TestMethodCsPlayfieldName()
        {
            var lcdMod = new EmpyrionScripting();
            EmpyrionScripting.Configuration.Current.CsScriptsAllowed = CsScriptsAllowed.Everywhere;
            var pf = new PlayfieldScriptData(lcdMod);

            var dict = new ConcurrentDictionary<string, object>();

            var data = Substitute.For<IScriptRootData>();
            var playfield = Substitute.For<IPlayfield>();
            playfield.Name.Returns("PlayfieldTestName");
            data.P = new PlayfieldData(playfield);
            data.GetPersistendData().ReturnsForAnyArgs(dict);
            data.Data = dict;

            data.CycleCounter.Returns(0);
            Assert.AreEqual("PlayfieldTestName", lcdMod.ExecuteCsScript(pf, data, "return P.Name;"));
        }

        [TestMethod]
        public void TestMethodCsExtMethods()
        {
            var lcdMod = new EmpyrionScripting();
            EmpyrionScripting.Configuration.Current.CsScriptsAllowed = CsScriptsAllowed.Everywhere;
            var pf = new PlayfieldScriptData(lcdMod);

            var dict = new ConcurrentDictionary<string, object>();

            var data = Substitute.For<IScriptRootData>();
            var playfield = Substitute.For<IPlayfield>();
            playfield.Name.Returns("PlayfieldTestName");
            data.P = new PlayfieldData(playfield);
            data.GetPersistendData().ReturnsForAnyArgs(dict);
            data.Data = dict;

            data.CycleCounter.Returns(0);
            Assert.AreEqual("a-a1", lcdMod.ExecuteCsScript(pf, data, "return string.Join(\"-\", new[]{\"a\", \"b\", \"a1\"}.GetUniqueNames(\"a*\"));"));
        }

        [TestMethod]
        public void TestMethodFillFuel()
        {
            ConveyorHelpers.CreateDeviceLock = (R, P, S, V) => new MockDeviceLock();

            var lcdMod = new EmpyrionScripting();
            EmpyrionScripting.Configuration.Current.CsScriptsAllowed = CsScriptsAllowed.Everywhere;
            var pf = new PlayfieldScriptData(lcdMod);

            var dict = new ConcurrentDictionary<string, object>();

            var data = Substitute.For<IScriptRootData>();
            var playfield = Substitute.For<IPlayfield>();
            playfield.Name.Returns("PlayfieldTestName");
            data.CsRoot.Returns(new CsHelper.CsScriptFunctions(data));

            var e = Substitute.For<IEntityData>();
            var s = Substitute.For<IStructureData>();
            var egsFuelTank = Substitute.For<IStructureTank>();

            int fuelTankContent = 5000;

            egsFuelTank.Capacity.Returns(10000);
            egsFuelTank.Content.ReturnsForAnyArgs(ci => fuelTankContent);
            egsFuelTank.UsesIntegerAmounts.Returns(true);
            egsFuelTank.When(x => x.AddContent(4500)).Do(x => fuelTankContent += 4500);

            var fuelTank = new StructureTank(egsFuelTank, StructureTankType.Fuel);
            s.FuelTank.Returns(fuelTank);

            var c = Substitute.For<IContainer>();

            ItemsSource boxItemContent = new ItemsSource()
            {
                E = e,
                Id = 2373,
                Count = 500,
                CustomName = "Box1",
                Container = c,
            };
            var itemSource = new List<ItemsSource>{ boxItemContent };

            c.RemoveItems(2373, 16).Returns(ci => { boxItemContent.Count -= 15; return 1; });

            var items = new[] { new ItemsData() {
                 Count = 50,
                 Id = 2373,
                 Key = "Fuelcell",
                 Name = "Energie",
                 Source = itemSource,
            } };
            s.Items.Returns(items);

            e.S.Returns(s);
            data.E.Returns(e);
            data.P = new PlayfieldData(playfield);
            data.GetPersistendData().ReturnsForAnyArgs(dict);
            data.Data = dict;
            data.DeviceLockAllowed.Returns(true);

            data.CycleCounter.Returns(0);
            Assert.AreEqual("Id:2373 Count:15 Source:Box1", lcdMod.ExecuteCsScript(pf, data, @"
                var mi = CsRoot.Fill(E.S.Items.First(I => I.Id == 2373), E.S, StructureTankType.Fuel)[0];
                return $""Id:{mi.Id} Count:{mi.Count} Source:{mi.Source}"";
            "));

            Assert.AreEqual(9500, fuelTankContent, "FuelTankContext");
            Assert.AreEqual(485, boxItemContent.Count, "BoxItemContent");
        }

        [TestMethod]
        public void TestMethodItemsMoveToOtherEntity()
        {
            ConveyorHelpers.CreateDeviceLock = (R, P, S, V) => new MockDeviceLock();
            
            // SV ==================================================================
            var SVboxA = Substitute.For<IContainer>();
            SVboxA.RemoveItems(1, int.MaxValue).Returns(10);
            SVboxA.AddItems(1, 1).Returns(0);
            SVboxA.AddItems(1, 10).Returns(C => throw new Exception("in the same box"));

            var SVboxB = Substitute.For<IContainer>();
            SVboxB.AddItems(1, 10).Returns(1);

            var esSV = Substitute.For<IStructure>();
            esSV.GetDevice<IContainer>("BoxA").Returns(SVboxA);
            esSV.GetDevice<IContainer>("BoxB").Returns(SVboxB);

            var eeSV = Substitute.For<IEntity>();
            eeSV.Structure.Returns(esSV);

            var eSV = Substitute.For<IEntityData>();
            eSV.Name.Returns("SVa");
            eSV.GetCurrent().Returns(eeSV);

            var sSV = Substitute.For<IStructureData>();
            sSV.AllCustomDeviceNames.Returns(new[] { "BoxA", "BoxB", "BoxC", "BoxX" });
            sSV.Items.Returns(new[] {
                new ItemsData(){ Id = 1, Count = 11, Name = "a", Source = new []{ new ItemsSource() { CustomName = "BoxA", Id = 1, Count = 10, Container = SVboxA, E = eSV } }.ToList() },
                new ItemsData(){ Id = 2, Count = 20, Name = "b", Source = new []{ new ItemsSource() { CustomName = "BoxB", Id = 2, Count = 20, Container = SVboxB, E = eSV } }.ToList() },
                new ItemsData(){ Id = 3, Count = 30, Name = "c", Source = new []{ new ItemsSource() { CustomName = "BoxC", Id = 3, Count = 30 } }.ToList()  },
            });
            sSV.ContainerSource.Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, ContainerSource>()
            {
                ["BoxA"] = new ContainerSource() { Container = SVboxA, CustomName = "BoxA" },
                ["BoxB"] = new ContainerSource() { Container = SVboxB, CustomName = "BoxB" },
            });
            sSV.GetCurrent().Returns(esSV);
            sSV.E.Returns(eSV);
            eSV.S.Returns(sSV);

            // CV ==================================================================
            var CVboxA = Substitute.For<IContainer>();
            CVboxA.RemoveItems(1, int.MaxValue).Returns(10);
            CVboxA.AddItems(1, 1).Returns(0);
            CVboxA.AddItems(1, 10).Returns(C => throw new Exception("in the same box"));

            var CVboxB = Substitute.For<IContainer>();
            CVboxB.AddItems(1, 10).Returns(1);

            var esCV = Substitute.For<IStructure>();
            esCV.GetDevice<IContainer>("BoxA").Returns(CVboxA);
            esCV.GetDevice<IContainer>("BoxB").Returns(CVboxB);

            var eeCV = Substitute.For<IEntity>();
            eeCV.Structure.Returns(esCV);

            var eCV = Substitute.For<IEntityData>();
            eCV.Name.Returns("CVb");
            eCV.GetCurrent().Returns(eeCV);

            var sCV = Substitute.For<IStructureData>();
            sCV.AllCustomDeviceNames.Returns(new[] { "BoxA", "BoxB", "BoxC", "BoxX" });
            sCV.Items.Returns(new[] {
                new ItemsData(){ Id = 1, Count = 11, Name = "a", Source = new []{ new ItemsSource() { CustomName = "BoxA", Id = 1, Count = 10, Container = CVboxA, E = eCV } }.ToList() },
                new ItemsData(){ Id = 2, Count = 20, Name = "b", Source = new []{ new ItemsSource() { CustomName = "BoxB", Id = 2, Count = 20, Container = CVboxB, E = eCV } }.ToList() },
                new ItemsData(){ Id = 3, Count = 30, Name = "c", Source = new []{ new ItemsSource() { CustomName = "BoxC", Id = 3, Count = 30 } }.ToList()  },
            });
            sCV.ContainerSource.Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, ContainerSource>()
            {
                ["BoxA"] = new ContainerSource() { Container = CVboxA, CustomName = "BoxA" },
                ["BoxB"] = new ContainerSource() { Container = CVboxB, CustomName = "BoxB" },
            });
            sCV.GetCurrent().Returns(esCV);
            sCV.E.Returns(eCV);
            eCV.S.Returns(sCV);

            sCV.DockedE.Returns(new[] { eSV });

            // MOVE ==================================================================

            var lcdData = Substitute.For<IScriptRootData>();
            lcdData.DeviceLockAllowed.Returns(true);
            lcdData.CsRoot.Returns(new CsHelper.CsScriptFunctions(lcdData));

            lcdData.E.Returns(eCV);
            lcdData.E.S.Returns(sCV);
            var lcdMod = new EmpyrionScripting();
            var s = new StringBuilder();
            EmpyrionScripting.Configuration.Current.CsScriptsAllowed = CsScriptsAllowed.Everywhere;
            var pf = new PlayfieldScriptData(lcdMod);
            Assert.AreEqual("SVa:1#9 SVa:BoxA->CVb:BoxB", lcdMod.ExecuteCsScript(pf, lcdData, @"
                E.S.DockedE.ForEach(e => { 
                    Console.Write(e.Name + "":"");
                    CsRoot.Items(e.S, ""BoxA"")
                        .ForEach(i => {
                            CsRoot.Move(i, E.S, ""Box*"").ForEach(mi => {
                                Console.Write($""{mi.Id}#{mi.Count} {mi.SourceE.Name}:{mi.Source}->{mi.DestinationE.Name}:{mi.Destination}"");
                            });
                        });
                });
            "));
        }


    }
}
