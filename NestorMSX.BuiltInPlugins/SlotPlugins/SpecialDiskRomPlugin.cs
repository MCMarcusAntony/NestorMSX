﻿using System;
using System.Collections.Generic;
using System.IO;
using Konamiman.NestorMSX.BuiltInPlugins.MemoryTypes;
using Konamiman.NestorMSX.Misc;
using Konamiman.Z80dotNet;

namespace Konamiman.NestorMSX.Plugins
{
    [NestorMSXPlugin("Special DiskROM")]
    public class SpecialDiskRomPlugin
    {
        private const int BDOS = 0xFB03;    //as defined in dskbasic.mac

        private readonly string fileName;
        private readonly DosFunctionCallExecutor dosFunctionsExecutor;

        public SpecialDiskRomPlugin(PluginContext context, IDictionary<string, object> pluginConfig)
        {
            if (!pluginConfig.ContainsKey("file"))
                throw new InvalidOperationException("No 'file' key in config file");

            fileName = pluginConfig.GetMachineFilePath(pluginConfig.GetValue<string>("file"));

            context.Cpu.BeforeInstructionFetch += Z80OnBeforeInstructionFetch;
            var filesystemBasePath = pluginConfig.GetValue<string>("filesystemBasePath").AsAbsolutePath();
            dosFunctionsExecutor = new DosFunctionCallExecutor(context.Cpu.Registers, context.SlotsSystem, filesystemBasePath);
        }

        private void Z80OnBeforeInstructionFetch(object sender, BeforeInstructionFetchEventArgs e)
        {
            if (((IZ80Processor)sender).Registers.PC == BDOS)
            {
                dosFunctionsExecutor.ExecuteFunctionCall();
                ((IZ80Processor)sender).ExecuteRet();
            }
        }

        public IMemory GetMemory()
        {
            return new PlainRom(File.ReadAllBytes(fileName), page: 1);
        }
    }
}