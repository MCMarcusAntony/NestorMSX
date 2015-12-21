﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Konamiman.NestorMSX.Exceptions;
using Konamiman.NestorMSX.Hardware;
using Konamiman.NestorMSX.Host;
using Konamiman.NestorMSX.Misc;
using Konamiman.Z80dotNet;

namespace Konamiman.NestorMSX.Emulator
{
    /// <summary>
    /// Full environment for running a MSX emulation.
    /// </summary>
    public class MsxEmulationEnvironment
    {
        private const int BDOS = 0xFB03;    //as defined in dskbasic.mac

        private DosFunctionCallExecutor dosFunctionsExecutor;
        private MsxEmulator emulator;
        private IDictionary<string, object> machineConfig;

        public IKeyEventSource KeyboardEventSource { get; }
        public EmulatorHostForm HostForm { get; }
        public IExternallyControlledSlotsSystem SlotsSystem { get; }
        public IExternallyControlledTms9918 Vdp { get; set; }
        public IZ80Processor Z80 { get; }
        public IKeyboardController KeyboardController { get; }

        public MsxEmulationEnvironment(Configuration config)
        {
            LoadMachineConfig(config);

            Z80 = CreateCpu(config);

            SlotsSystem = CreateSlotsSystem(config);
            
            HostForm = CreateHostForm(config, Z80);
            KeyboardEventSource = HostForm;

            Vdp = CreateVdp(config, HostForm);
            HostForm.Vdp = Vdp;

            KeyboardController = CreateKeyboardController(config, HostForm);
            
            ConfigureDiskRom(config, SlotsSystem, Z80);
            
            var hardware = new MsxHardwareSet {
                Cpu = Z80,
                KeyboardController = KeyboardController,
                SlotsSystem = SlotsSystem,
                Vdp = Vdp
            };

            emulator = new MsxEmulator(hardware);
        }

        private void LoadMachineConfig(Configuration config)
        {
            var machineName = config.MachineName;
            var folder = Path.Combine("machines", machineName).AsAbsolutePath();
            if(!Directory.Exists(folder))
                throw new ConfigurationException($"Machine folder not found for '{machineName}'");

            var configFilePath = Path.Combine(folder, "machine.config");
            if(!File.Exists(configFilePath))
                throw new ConfigurationException($"machine.config file not found for '{machineName}'");

            var json = File.ReadAllText(configFilePath);
            try
            {
                machineConfig = (IDictionary<string, object>)JsonParser.Parse(json);
            }
            catch(Exception ex)
            {
                throw new ConfigurationException($"Error when reading machine.config file for '{machineName}': {ex.Message}");
            }
        }

        private void ConfigureDiskRom(Configuration config, IExternallyControlledSlotsSystem slots, IZ80Processor z80)
        {
            if (config.DiskRomFile != null)
            {
                slots.SetSlotContents(1, new PlainRom(FileUtils.ReadAllBytes(config.DiskRomFile), 1));
                z80.BeforeInstructionFetch += Z80OnBeforeInstructionFetch;
                dosFunctionsExecutor = new DosFunctionCallExecutor(z80.Registers, slots, config.FilesystemBaseLocation);
            }
        }

        private KeyboardController CreateKeyboardController(Configuration config, IKeyEventSource keyEventSource)
        {
            return new KeyboardController(keyEventSource, FileUtils.ReadAllText(config.KeymapFile));
        }

        private IExternallyControlledTms9918 CreateVdp(Configuration config, IDrawingSurface drawingSurface)
        {
            return new Tms9918(new DisplayRenderer(new GraphicsBasedDisplay(drawingSurface, config), config), config);
        }

        private EmulatorHostForm CreateHostForm(Configuration config, IZ80Processor cpu)
        {
            return new EmulatorHostForm(cpu, config);
        }

        private IExternallyControlledSlotsSystem CreateSlotsSystem(Configuration config)
        {
            foreach (var slotConfig in machineConfig["slots"] as IDictionary<string, object>)
            {
                SlotNumber slotNumber;
                if (!SlotNumber.TryParse(slotConfig.Key, out slotNumber))
                    continue;

                var configValues = (IDictionary<string, object>)slotConfig.Value;
            }

            //WIP...

            var slots = new SlotsSystem();

            slots.SetSlotContents(0, new PlainRom(FileUtils.ReadAllBytes(config.BiosFile)));

            if(config.Slot2RomFile != null)
                slots.SetSlotContents(2, new PlainRom(FileUtils.ReadAllBytes(config.Slot2RomFile), 1));

            var ram = new PlainMemory(ushort.MaxValue + 1);
            slots.SetSlotContents(3, ram);

            return slots;
        }

        private IZ80Processor CreateCpu(Configuration config)
        {
            var z80 = new Z80Processor();

            if(config.CpuSpeedInMHz == 0)
                z80.ClockSynchronizer = null;
            else if(config.CpuSpeedInMHz < 0.001M || config.CpuSpeedInMHz > 100)
                throw new ConfigurationException("CPU speed must be either zero or a value between 0.001 and 100");
            else
                z80.ClockFrequencyInMHz = config.CpuSpeedInMHz;

            return z80;
        }

        private void Z80OnBeforeInstructionFetch(object sender, BeforeInstructionFetchEventArgs eventArgs)
        {
            if(((IZ80Processor)sender).Registers.PC == BDOS)
            {
                dosFunctionsExecutor.ExecuteFunctionCall();
            }
        }

        public void Run()
        {
            KeyboardEventSource.StartGeneratingKeyEvents();
            new Task(() => emulator.Run()).Start();
            Application.Run(HostForm);
        }
    }
}
