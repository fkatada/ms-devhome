﻿// Copyright (c) Microsoft Corporation and Contributors
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DevHome.SetupFlow.Common.Exceptions;
using DevHome.SetupFlow.Common.Helpers;
using Microsoft.Management.Configuration;
using Microsoft.Management.Configuration.Processor;
using Windows.Storage;

namespace DevHome.SetupFlow.Common.Configuration;

/// <summary>
/// Helper for applying a configuration file. This exists so that we can
/// use it in an elevated or non-elevated context.
/// </summary>
public class ConfigurationFileHelper
{
    public class ApplicationResult
    {
        public ApplyConfigurationSetResult Result
        {
            get;
        }

        public bool Succeeded => Result.ResultCode == null;

        public bool RequiresReboot => Result.UnitResults.Any(result => result.RebootRequired);

        public Exception ResultException => Result.ResultCode;

        public ApplicationResult(ApplyConfigurationSetResult result)
        {
            Result = result;
        }
    }

    private readonly StorageFile _file;
    private ConfigurationProcessor _processor;
    private ConfigurationSet _configSet;

    public ConfigurationFileHelper(StorageFile file)
    {
        _file = file;
    }

    public async Task OpenConfigurationSetAsync()
    {
        try
        {
            var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidDataException();
            var rootDirectory = Path.GetDirectoryName(assemblyDirectory) ?? throw new InvalidDataException();
            var modulesPath = Path.Combine(rootDirectory, "ExternalModules");

            var properties = new ConfigurationProcessorFactoryProperties();
            properties.AdditionalModulePaths = new List<string>() { modulesPath };
            var factory = new ConfigurationSetProcessorFactory(ConfigurationProcessorType.Hosted, properties);
            factory.MinimumLevel = DiagnosticLevel.Verbose;
            factory.Diagnostics += (sender, args) => LogConfigurationDiagnostics("ConfigurationFactory", args);

            _processor = new ConfigurationProcessor(factory);
            _processor.MinimumLevel = DiagnosticLevel.Verbose;
            _processor.Diagnostics += (sender, args) => LogConfigurationDiagnostics("ConfigurationProcessor", args);

            Log.Logger?.ReportInfo(Log.Component.Configuration, $"Opening configuration set from path {_file.Path}");
            var openResult = _processor.OpenConfigurationSet(await _file.OpenReadAsync());
            _configSet = openResult.Set;
            if (_configSet == null)
            {
                throw new OpenConfigurationSetException(openResult.ResultCode, openResult.Field);
            }
        }
        catch
        {
            _processor = null;
            _configSet = null;
            throw;
        }
    }

    public async Task<ApplicationResult> ApplyConfigurationAsync()
    {
        if (_processor == null || _configSet == null)
        {
            throw new InvalidOperationException();
        }

        Log.Logger?.ReportInfo(Log.Component.Configuration, "Starting to apply configuration set");
        var result = await _processor.ApplySetAsync(_configSet, ApplyConfigurationSetFlags.None);
        Log.Logger?.ReportInfo(Log.Component.Configuration, $"Apply configuration finished. HResult: {result.ResultCode?.HResult}");
        return new ApplicationResult(result);
    }

    private void LogConfigurationDiagnostics(string sourceComponent, DiagnosticInformation diagnosticInformation)
    {
        switch (diagnosticInformation.Level)
        {
            case DiagnosticLevel.Verbose:
                Log.Logger?.ReportDebug(Log.Component.Configuration, sourceComponent, diagnosticInformation.Message);
                return;
            case DiagnosticLevel.Warning:
                Log.Logger?.ReportWarn(Log.Component.Configuration, sourceComponent, diagnosticInformation.Message);
                return;
            case DiagnosticLevel.Error:
                Log.Logger?.ReportError(Log.Component.Configuration, sourceComponent, diagnosticInformation.Message);
                return;
            case DiagnosticLevel.Critical:
                Log.Logger?.ReportCritical(Log.Component.Configuration, sourceComponent, diagnosticInformation.Message);
                return;
            case DiagnosticLevel.Informational:
            default:
                Log.Logger?.ReportInfo(Log.Component.Configuration, sourceComponent, diagnosticInformation.Message);
                return;
        }
    }
}