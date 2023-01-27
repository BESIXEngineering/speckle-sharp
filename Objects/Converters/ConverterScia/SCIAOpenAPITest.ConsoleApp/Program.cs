using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Speckle.Core.Api;
using Speckle.Core.Api.SubscriptionModels;
using Speckle.Core.Credentials;
using Speckle.Core.Kits;
using Speckle.Core.Models;

using SCIA.OpenAPI;
using SCIA.OpenAPI.Utils;
using SCIA.OpenAPI.Results;
using SCIA.OpenAPI.StructureModelDefinition;
using Results64Enums;

using SciaTools.AdmToAdm.AdmSignalR.Models.ModelModification;
using SciaTools.Kernel.ModelExchangerExtension.Models.Exchange;

using CSInfrastructure.IOC;
using ModelExchanger.AnalysisDataModel;
using ModelExchanger.AnalysisDataModel.Models;
using ModelExchanger.AnalysisDataModel.Loads;
using ModelExchanger.AnalysisDataModel.StructuralElements;
using ModelExchanger.Excel.Conversion.Contracts;
using ModelExchanger.Excel.Integration.Bootstrapper;
using ModelExchanger.AnalysisDataModel.Enums;
using ModelExchanger.AnalysisDataModel.Integration.Bootstrapper;
using ModelExchanger.AnalysisDataModel.Contracts;
using ModelExchanger.AnalysisDataModel.Comparison;


namespace SCIAOpenAPITest.ConsoleApp
{
  class Program
  {
    #region ### MAIN SETTINGS ###
    /// <summary>
    /// Full path to the SCIA installation folder
    /// </summary>
    const string SCIA_APP_PATH = @"C:\Program Files\SCIA\Engineer22.0\";

    /// <summary>
    /// App name of the SCIAConverter to use
    /// </summary>
    const string SCIA_CONVERTER_APP_NAME = "SCIA_22-0";
    /// <summary>
    /// Folder in which the SCIA API logs will be written
    /// </summary>
    const string LOG_PATH = @"c:\TEMP\SCIA.OpenAPI\MyLogsTemp";

    /// <summary>
    /// Determine whether a websocket should be opened to listen and react to new Speckle commits
    /// </summary>
    const bool OPEN_SOCKET = false;
    #endregion


    #region ### MAIN METHOD AND CACHE ###
    private static Client SpeckleClient { get; set; }
    private static StreamWrapper SpeckleStreamWrapper { get; set; }

    private static SCIA.OpenAPI.Environment SCIAenv { get; set; }
    private static EsaProject Project { get; set; }
    private static AnalysisModel ContextModel { get; set; }


    public static void Main(string[] args)
    {
      SciaOpenApiAssemblyResolve();
      Run();
    }

    public static void Run()
    {
      // TEST 0 - Launch SCIA OpenAPI environment
      // InitSCIAProject().Wait();

      // TEST 1 - Run SCIA through API and load in a Speckle stream
      string branchUrl = @"https://speckle.xyz/streams/959e339e50/branches/testmodel01";
      // RunSCIAFromSpeckleStream(branchUrl);  // TODO check Speckle account connection issues

      // TEST 2 - Import a SAF file as ADM, convert to Speckle and back to ADM, and compare
      //  In an ideal world, the two models should be identical, but that is far from the case for the moment;
      string pathToSAF = @"..\..\samples\SAF_example_HOUSE_metric_ZYX_210.xlsx";
      ImportExportCompare(pathToSAF);

      // TEST 3 - Launch calculation and get results
      string pathToEsaProject = @"..\..\samples\SAF_example_STEEL_HALL_metrix_ZYX_210_calculated.esa";
      // OpenComputeGetResults(pathToEsaProject, false);

      // Catch any exceptions to close the clients gracefully
      try
      {
      }
      catch (Exception e)
      {
        Console.WriteLine("Oops, something went wrong!");
        Console.WriteLine(e);
      }
      finally
      {
        Console.WriteLine("\n-- Press any key to exit --");
        Console.ReadKey(true);

        // End the SCIA connection, if any
        CloseSCIAEnvironment();
        // End the Speckle connection, if any
        SpeckleClient?.Dispose();
      }
    }
    #endregion


    #region ### TEST 1: SCIA from Speckle stream ###
    public static void RunSCIAFromSpeckleStream(string branchUrl)
    {
      // Solve issue in loading BESIX kit which is dependent on Objects kit
      // ObjectsKitAssemblyResolve();

      // Get the kit with the correct converter
      //PrintKits();

      // Informative list accounts
      //PrintAccounts();

      Console.WriteLine($"Getting data from stream at {branchUrl}");
      SpeckleStreamWrapper = new StreamWrapper(branchUrl);

      ReceiveStream();

      UIRequestUserAction();
    }

    #region ### Console UI request action ###
    private static void UIRequestUserAction()
    {
      while (true)
      {
        Console.WriteLine("What would you like to do now? (Press esc to exit)" +
            "\n\t1. add beam between nodes" +
            "\n\t2. calculate model and read results");
        var key = Console.ReadKey();

        if (key.Key == ConsoleKey.Escape)
        {
          break;
        }
        else if (key.Key == ConsoleKey.NumPad1)
        {
          UIAddBeam();
        }
        else if (key.Key == ConsoleKey.NumPad2)
        {
          UICalculateAndReadResults();
        }
        else
        {
          Console.WriteLine("⚠ Invalid key");
        }
      }
    }

    private static void UIAddBeam()
    {
      Console.WriteLine("\nCreating a beam");
      Console.WriteLine("> Give name of the beam");
      string name = Console.ReadLine();
      Console.WriteLine("> Give name of first node");
      string node1 = Console.ReadLine();
      Console.WriteLine("> Give name of second node");
      string node2 = Console.ReadLine();
      Console.WriteLine("> Give name of cross section");
      string css = Console.ReadLine();
      Add1DMember(name, node1, node2, css);
    }

    private static void UICalculateAndReadResults()
    {
      Console.WriteLine("\nCommencing calculation");
      var resultsApi = RunCalculation();

      if (resultsApi == null)
      {
        Console.WriteLine("Failed to load results");
        return;
      }

      Console.WriteLine("Reading result");

      // UI select a beam
      var beams = ContextModel.OfType<StructuralCurveMember>().ToList();
      Console.WriteLine("> Pick a beam");

      for (int i = 0; i < beams.Count(); i++)
      {
        Console.WriteLine($"  {i}. Beam {beams[i].Name}");
      }
      var key = Console.ReadKey();
      var beamIndex = Convert.ToInt32(key.KeyChar.ToString());
      if (beamIndex >= beams.Count)
      {
        Console.WriteLine("--Invalid input--");
        return;
      }
      string beamName = beams[beamIndex].Name;

      // Get the first loadcase
      var loadCase = ContextModel.OfType<StructuralLoadCase>().FirstOrDefault();

      //Create container for 1D results
      Result IntFor1Db1 = new Result();
      //Results key for internal forces on beam 1
      ResultKey keyIntFor1Db1 = new ResultKey
      {
        CaseType = eDsElementType.eDsElementType_LoadCase,
        CaseId = loadCase.Id,
        EntityType = eDsElementType.eDsElementType_Beam,
        EntityName = beamName,
        Dimension = eDimension.eDim_1D,
        ResultType = eResultType.eFemBeamInnerForces,
        CoordSystem = eCoordSystem.eCoordSys_Local
      };
      //Load 1D results based on results key
      IntFor1Db1 = resultsApi.LoadResult(keyIntFor1Db1);
      if (IntFor1Db1 != null)
      {
        Console.WriteLine(IntFor1Db1.GetTextOutput());
        var N = IntFor1Db1.GetMagnitudeName(0);
        var Nvalue = IntFor1Db1.GetValue(0, 0);
        Console.WriteLine(N);
        Console.WriteLine(Nvalue);
      }

      resultsApi.Dispose();
    }
    #endregion

    #endregion


    #region ### TEST 2: Two-way conversion (SAF > Speckle > SAF) and comparison ###
    public static void ImportExportCompare(string path)
    {
      if (!File.Exists(path))
      {
        Console.WriteLine($"Couldn't find SAF file at '{Path.GetFullPath(path)}'");
        return;
      }

      // Import the SAF model
      Console.WriteLine("\n#####################################\n# Importing analysis model from SAF #\n#####################################\n");
      StartTimer();
      var admModel1 = ImportFromSAF(path);
      WriteTime();

      // Convert to Speckle
      Base speckleModel = null;

      Console.WriteLine("\n\n#############################\n# Converting ADM to Speckle #\n#############################\n");
      StartTimer();
      speckleModel = ConvertToSpeckle(admModel1, out ProgressReport speckleConversionReport);
      WriteTime();

      Console.WriteLine("\n### CONVERSION LOG ###");
      Console.WriteLine(speckleConversionReport.ConversionLogString);

      Console.WriteLine("\n### SUMMARY OF ERRORS ###");
      foreach (var ex in speckleConversionReport.ConversionErrors.Select(x => x.Message).Distinct())
      {
        Console.WriteLine($"- {ex}");
      }

      // Convert back to ADM
      Console.WriteLine("\n\n#############################\n# Converting Speckle to ADM #\n#############################\n");
      StartTimer();
      var admModel2 = new AnalysisModel();
      admModel2 = ConvertToADM(speckleModel, out ProgressReport admConversionReport, admModel2);
      WriteTime();

      Console.WriteLine("\n### CONVERSION LOG ###");
      Console.WriteLine(admConversionReport.ConversionLogString);

      Console.WriteLine("\n### SUMMARY OF ERRORS ###");
      foreach (var ex in admConversionReport.ConversionErrors.Select(x => x.Message).Distinct())
      {
        Console.WriteLine($"- {ex}");
      }

      // Compare the models and print the results
      Console.WriteLine("\n\n########################\n# Comparing ADM models #\n########################\n");
      StartTimer();
      CompareModels(admModel1, admModel2);
      WriteTime();

      // Export back to SAF
      // Debug.WriteLine("Exporting ADM to SAF...");
      // ExportToSAF(admModel, path);
    }

    public static void CompareModels(AnalysisModel model1, AnalysisModel model2)
    {
      IBootstrapper bootstrapper = new AnalysisDataModelBootstrapper();
      using (IScopedServiceProvider scope = bootstrapper.CreateThreadedScope())
      {
        IAnalysisModelComparisonService comparisonService = scope.GetService<IAnalysisModelComparisonService>();

        // Compare the 2 models
        AnalysisModelComparison result = comparisonService.Compare(model1, model2);

        Console.WriteLine("\n### COMPARISON LOG ###");
        Console.WriteLine($"There are {model1.Count - result.Updated.Count - result.Deleted.Count} identical objects"); // Outputs "There are 1 new objects"
        Console.WriteLine($"There are {result.New.Count} new objects"); // Outputs "There are 1 new objects"
        Console.WriteLine($"There are {result.Updated.Count} updated objects"); // Outputs "There are 1 updated objects"
        Console.WriteLine($"There are {result.Deleted.Count} deleted objects"); // Outputs "There are 1 deleted objects"

        Console.WriteLine("\n*NEW*");
        foreach (var group in result.New.Select(kvp => kvp.Value).GroupBy(v => v.Type))
        {
          Console.WriteLine($" - {group.Key} ({string.Join(", ", group.Select(v => v.Name))})");
        }

        Console.WriteLine("\n*UPDATED*");
        foreach (var group in result.Updated.Select(kvp => kvp.Value).GroupBy(v => v.Type))
        {
          // List differences for a single element of each object type
          Console.WriteLine($" - {group.Key} ({string.Join(", ", group.Select(v => v.Name))})");
          ListModelObjectDifferences(model1, model2, group.First().Id);
        }

        Console.WriteLine("\n*DELETED*");
        foreach (var group in result.Deleted.Select(kvp => kvp.Value).GroupBy(v => v.Type))
        {
          Console.WriteLine($" - {group.Key} ({string.Join(", ", group.Select(v => v.Name))})");
        }
      }
    }

    private static void ListModelObjectDifferences(AnalysisModel model1, AnalysisModel model2, Guid id)
    {
      IAnalysisObject object1 = model1.First(obj => obj.Id == id);
      IAnalysisObject object2 = model2.First(obj => obj.Id == id);

      Type t = object1.GetType();
      if (object2.GetType() != t) throw new Exception("Can't compare apples and oranges");

      Console.WriteLine($"\n   Comparing {t} {object1.Name} ({id}):");
      foreach (PropertyInfo pi in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        if (pi.Name == "Item") continue;

        object value1 = t.GetProperty(pi.Name)?.GetValue(object1, null);
        object value2 = t.GetProperty(pi.Name)?.GetValue(object2, null);

        if (value1 != value2 && (value1 == null || !value1.Equals(value2)))
        {
          Console.WriteLine($"\t- Property {pi.Name} (of type {value1?.GetType().Name}) = {GetPropertyValueString(value1)} >>> {GetPropertyValueString(value2)}");
        }
      }
      Console.WriteLine("\n");
    }

    private static string GetPropertyValueString(object value)
    {
      if (value == null) return "-null-";
      if (value is ICollection coll)
      {
        var count = coll.Count;
        if (count == 0) return "-empty collection-";

        var admObjs = coll.OfType<IAnalysisObject>();
        var typeName = "";
        var itemString = "";

        if (admObjs.Any())
        {
          typeName = admObjs.First().GetType().Name;
          itemString = string.Join(", ", admObjs.Select(x => x.Name));
        }
        else
        {
          var objs = coll.OfType<object>();
          typeName = objs.First().GetType().Name;
          itemString = string.Join(", ", objs.Select(x => x.ToString()));
        }
        return $"{typeName} x {count}: {itemString}"; 
      }
      return value.ToString();
    }
    #endregion


    #region ### TEST 3: results API ###
    private static void OpenComputeGetResults(string pathToEsa, bool allResults)
    {
      InitSCIAProject(Path.GetFullPath(pathToEsa)).Wait();
      Console.WriteLine($"Opened model at {pathToEsa}");

      if (Project.Model.InitializeResultsAPI() == null)
      {
        while (true)
        {
          Console.WriteLine("No results available, launch calculation now? (Y/N)");
          var key = Console.ReadKey();

          if (key.Key == ConsoleKey.Y)
          {
            break;
          }
          else if (key.Key == ConsoleKey.N || key.Key == ConsoleKey.Escape)
          {
            Console.WriteLine("Ending routine...");
            return;
          }
          else
          {
            Console.WriteLine("Invalid key!");
          }
        }
        Project.RunCalculation();
      }
      //  Project.Model.RefreshModel_FromSCIAEngineer();

      while (true)
      {
        if (allResults)
        {
          if (!UIRequestAllResults(Project.Model)) break;
        }
        else
        {
          if (!UIRequestResult(Project.Model)) break;
        }
      }
    }

    #region Console UI Result Query

    #region UI ResultOptions
    private static eDsElementType[] ResultCaseOptions => new eDsElementType[]
    {
              eDsElementType.eDsElementType_LoadCase,
              eDsElementType.eDsElementType_Combination,
              eDsElementType.eDsElementType_CombiKey,
              eDsElementType.eDsElementType_NonlinearCombination,
              eDsElementType.eDsElementType_Class,
              eDsElementType.eDsElementType_MassCombination,
              eDsElementType.eDsElementType_Stability,
    };

    private static eDsElementType[] ResultEntOptions => new eDsElementType[]
    {
              eDsElementType.eDsElementType_None,
              eDsElementType.eDsElementType_Beam,
              eDsElementType.eDsElementType_Slab,
              eDsElementType.eDsElementType_Storey,
              eDsElementType.eDsElementType_Node,
              eDsElementType.eDsElementType_SubSlab,
              eDsElementType.eDsElementType_SectionOnSlab, // not yet supported!!!
              eDsElementType.eDsElementType_Boundary,
              eDsElementType.eDsElementType_Storey_RN,
              eDsElementType.eDsElementType_PointSupportPoint,
              eDsElementType.eDsElementType_PointSupportLine,
              eDsElementType.eDsElementType_LineSupportLine,
              eDsElementType.eDsElementType_LineSupportSurface,
              eDsElementType.eDsElementType_Connection,
              eDsElementType.eDsElementType_IntegrationStrip,
              eDsElementType.eDsElementType_Independent,
              eDsElementType.eDsElementType_IntegrationMember
    };

    private static eDimension[] ResultDimOptions => new eDimension[]
    {
              eDimension.eDim_undefined,
              eDimension.eDim_1D, //FB_1D //results on beams
              eDimension.eDim_2D, //FB_2D //2D macros
              eDimension.eDim_3D, //FB_3D //3D macros
              eDimension.eDim_reactionsPoint, //FB_REA_PT, // reactions in points
              eDimension.eDim_reactionsLine, //FB_REA_LIN, // reactions on lines
              eDimension.eDim_2DSection, //Results on 2d sections, not yet supported!!!
              eDimension.eDim_1DSection, //Results on 1d sections
              eDimension.eDim_Storey, //Results per storeys 
    };

    private static eResultType[] ResultTypeOptions => new eResultType[]
    {
              eResultType.eNone,
              // FEMBase results 2D    
              eResultType.eFemDeformations,
              eResultType.eFemInnerForces,
              eResultType.eFemStress,
              eResultType.eFemContactStress,
              eResultType.eFemRatedQuantitesForReinforcement,
              eResultType.eFemReinforcement,
              eResultType.eFemPowerLoadOfMacros2D,
              eResultType.eFemTemperatureLoadOfMacros2D,
              eResultType.eFemSubsoilOfMacros2D,
              eResultType.eFemParametersOfIsotopyOnMacros2D,
              eResultType.eFemSubsoilForSoilin,
              eResultType.eFemOtherDataForSoilin,
              eResultType.eFemCracks,
              eResultType.eFemStrains,
              eResultType.eFemPlasticStrains,
              eResultType.eReactionsNodes,
              // FEMBase results 1D:
              eResultType.eFemBeamDeformation,
              eResultType.eFemBeamInnerForces,
              eResultType.eFemBeamContactForces,
              // FemBase results per storeys:
              // eResultType.eStoreyData, eResultType.eStoreyDisplacements, eResultType.eStoreyAccelerations, eResultType.eStoreyForces, eResultType.eStoreyAccidentalTorsion, eResultType.eInterStoreyDrift, eResultType.eReservedForRelease2013_1,
              // Recalculated FEMBase results:
              eResultType.eFemBeamRelativeDeformation,
              eResultType.eFemResultingForces,
              //user - defined results:
              eResultType.eTestCheck,
              eResultType.eTimberCheckSLS,
              eResultType.eTimberCheckULS,
              eResultType.eDFDesignAsLongitudinal,
              eResultType.eDFCheckResponse,
              eResultType.eDFInternalForces,
              eResultType.eDFDesignShearNBR,
              eResultType.eDFUserTemplate,
              eResultType.e2DConcreteBrazil,
              eResultType.e1DConcreteECEN,
              eResultType.eCombinatorSlabStrain,
              eResultType.eCombinatorBeamStrain,
              eResultType.eMemberStress_Sigma,
              eResultType.eFibrePosition,
              eResultType.eFibreDeformation,
              eResultType.eAdaptiveMesh,
              eResultType.eELFLoads,
              eResultType.eEN_1993_SteelULS,
              eResultType.eEN_1993_SteelFire,
              eResultType.eIDEA_StatiCa_Connection,
              eResultType.eEN_1993_SteelSLS,
              eResultType.eSIA_263_SteelULS,
              eResultType.eSteel_SLS,
              eResultType.eEN_1999_AluminiumULS,
              eResultType.eLastResultType
    };

    private static eCoordSystem[] ResultCsOptions => new eCoordSystem[]
    {
                  eCoordSystem.eCoordSys_Local, // - reactions in the nodes will be returned in rotated coordinate system // - reactions on lines will be returned in the local coordinate system of the line // - deformations,internal forces and contact stresses on the 1D elements will be returned in the principal coordinate system of the 1D element // - deformations and internal forces on the 2D elements will be returned in the local coordinate system of the 2D macroelement.
                  eCoordSystem.eCoordSys_Global, // - reactions in the nodes will be returned in global coordinate system // - reactions on lines will be returned in the global coordinate system // - deformations and contact stresses on the beams will be returned in the global coordinate system // - inner forces on the beams will be returned in the coordinate system of the cross section // - deformations on the 2D elements will be returned in the global coordinate system
                  eCoordSystem.eCoordSys_User, // - deformations, ihned forces and contact stresses on the beams will be returned in the user coordinate system of the beam
                  eCoordSystem.eCoordSys_Principal, // - used only for storey results!
                  eCoordSystem.eCoordSys_ByMember
    };
    #endregion

    private static bool UIRequestAllResults(Structure model)
    {
      Console.WriteLine("\n*** Getting results ***\n");
      Console.WriteLine("~ write 'exit' to quit");

      ApiGuid caseId;
      if (!UIRequestSelection("the type of loading case for which results will be obtained",
          ResultCaseOptions, eDsElementType.eDsElementType_LoadCase, out eDsElementType caseElemType))
      {
        return false;
      }
      if (!UIRequestString("name of the loading case for which results will be obtained",
          "LC2", out string caseElemName))
      {
        return false;
      }
      else
      {
        try
        {
          caseId = model.FindGuid(caseElemName);
        }
        catch (ArgumentException ex)
        {
          Console.WriteLine($"Couldn't find element {caseElemName}: {ex.Message}");
          return true;
        }
      }

      if (!UIRequestSelection("the type of entity for which results will be obtained",
          ResultEntOptions, eDsElementType.eDsElementType_Node, out eDsElementType entElemType))
      {
        return false;
      }
      if (!UIRequestString("name of the entity/member (SEn name) for which you want to get results",
          "N1", out string entElemName))
      {
        return false;
      }

      if (!UIRequestSelection("the result coordinate system",
          ResultCsOptions, eCoordSystem.eCoordSys_Global, out eCoordSystem cs))
      {
        return false;
      }

      foreach (eDimension rDim in ResultDimOptions)
        foreach (eResultType rType in ResultTypeOptions)
        {
          PrintResult(model, caseElemType, caseId, entElemType, entElemName, rDim, rType, cs);
        }
      return true;
    }

    private static bool UIRequestResult(Structure model)
    {
      Console.WriteLine("\n*** Getting results ***\n");
      Console.WriteLine("~ write 'exit' to quit");

      ApiGuid caseId;
      if (!UIRequestSelection("the type of loading case for which results will be obtained",
          ResultCaseOptions, eDsElementType.eDsElementType_LoadCase, out eDsElementType caseElemType))
      {
        return false;
      }
      if (!UIRequestString("name of the loading case for which results will be obtained",
          "LC2", out string caseElemName))
      {
        return false;
      }
      else
      {
        try
        {
          caseId = model.FindGuid(caseElemName);
        }
        catch (ArgumentException ex)
        {
          Console.WriteLine($"Couldn't find element {caseElemName}: {ex.Message}");
          return true;
        }
      }

      if (!UIRequestSelection("the type of entity for which results will be obtained",
          ResultEntOptions, eDsElementType.eDsElementType_Node, out eDsElementType entElemType))
      {
        return false;
      }
      if (!UIRequestString("name of the entity/member (SEn name) for which you want to get results",
          "N1", out string entElemName))
      {
        return false;
      }

      if (!UIRequestSelection("the dimension of obtained results",
          ResultDimOptions, eDimension.eDim_reactionsPoint, out eDimension dimType))
      {
        return false;
      }

      if (!UIRequestSelection("the type of obtained results",
          ResultTypeOptions, eResultType.eReactionsNodes, out eResultType resType))
      {
        return false;
      }

      if (!UIRequestSelection("the result coordinate system",
          ResultCsOptions, eCoordSystem.eCoordSys_Global, out eCoordSystem cs))
      {
        return false;
      }

      //Console.WriteLine($"Getting result for case {caseElemType} {caseId}, entity {entElemType} {entElemName}, dimension {dimType}, result {resType}, coordinates {cs}");
      PrintResult(model, caseElemType, caseId, entElemType, entElemName, dimType, resType, cs);
      return true;
    }

    private static bool UIRequestSelection<T>(string label, T[] options, T defaultValue, out T result)
    {
      result = default;

      Console.WriteLine($"Specify {label}  (default = {defaultValue}):");

      var optionDict = new Dictionary<string, T>();

      for (int i = 0; i < options.Length; i++)
      {
        Console.WriteLine($" {i + 1} = {options[i]}");
        optionDict[(i + 1).ToString()] = options[i];
      }

      while (true)
      {
        var key = Console.ReadLine().Trim();

        if (optionDict.ContainsKey(key))
        {
          result = optionDict[key];
          return true;
        }
        else if ("exit".Equals(key, StringComparison.OrdinalIgnoreCase))
        {
          return false;
        }
        else if (string.IsNullOrEmpty(key))
        {
          result = defaultValue;
          return true;
        }
        Console.WriteLine("Invalid option");
      }
    }

    private static bool UIRequestEnum<T>(string label, out T result) where T : Enum
    {
      result = default;

      Console.WriteLine($"Specify {label}:");

      var options = new Dictionary<string, T>();

      foreach (T opt in Enum.GetValues(result.GetType()))
      {
        string numberString = Convert.ToInt32(opt).ToString();
        Console.WriteLine($" numberString = {opt}:");
        options[numberString] = opt;
      }

      while (true)
      {
        var key = Console.ReadLine().Trim();

        if (options.ContainsKey(key))
        {
          result = options[key];
          return true;
        }
        else if ("exit".Equals(key, StringComparison.OrdinalIgnoreCase))
        {
          return false;
        }
        Console.WriteLine("Invalid option");
      }
    }

    private static bool UIRequestString(string label, string defaultValue, out string result)
    {
      result = default;

      Console.WriteLine($"Specify {label} (default = {defaultValue}):");

      while (true)
      {
        var key = Console.ReadLine().Trim();

        if ("exit".Equals(key, StringComparison.OrdinalIgnoreCase))
        {
          return false;
        }

        if (string.IsNullOrEmpty(key))
        {
          result = defaultValue;
        }
        else
        {
          result = key;
        }
        return true;
      }
    }
    #endregion

    private static void PrintResult(
        Structure model,
        eDsElementType caseType, ApiGuid caseId,
        eDsElementType entityType, string entityName,
        eDimension dimensionType,
        eResultType resultType,
        eCoordSystem cs)
    {
      // Run calculation if needed
      var rapi = model.InitializeResultsAPI();

      try
      {
        // var caseId = model.FindGuid(caseName);
        ResultKey rKey = new ResultKey
        {
          CaseType = caseType,
          CaseId = caseId,
          EntityType = entityType,
          Dimension = dimensionType,
          ResultType = resultType,
          CoordSystem = cs,
          // Location = eResLocation.eCentres,
        };
        if (!string.IsNullOrEmpty(entityName))
        {
          rKey.EntityName = entityName;
        }

        Result result = rapi.LoadResult(rKey);
        if (result != null)
        {
          Console.WriteLine($"Results for case {caseType} {caseId}, entity {entityType} {entityName}, dimension {dimensionType}, result {resultType}:");
          Console.WriteLine(result.GetTextOutput());
        }
        else
        {
          Console.WriteLine($"Couldn't find result for case {caseType} {caseId}, entity {entityType} {entityName}, dimension {dimensionType}, result {resultType}");
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Failed to read results for case {caseType} {caseId}, entity {entityType} {entityName}, dimension {dimensionType}, result {resultType}: {ex.Message}");
        return;
      }
    }

    #endregion


    #region ### Conversion Speckle<>ADM ###
    public static AnalysisModel ConvertToADM(Base @base, out ProgressReport report, AnalysisModel admModel = null)
    {
      report = null;
      if (@base == null) throw new ArgumentNullException(nameof(@base));
      if (admModel == null) admModel = new AnalysisModel();

      ISpeckleConverter converter = GetSCIAConverter();

      // Set context document from SCIA in the converter
      converter.SetContextDocument(admModel);

      // Convert Speckle Base objects to SCIA ADM
      converter.ConvertToNative(@base);
      report = converter.Report;

      return admModel;
    }

    public static AnalysisModel ConvertToADM(List<Base> objects, out ProgressReport report, AnalysisModel admModel = null)
    {
      report = null;
      if (admModel == null) admModel = new AnalysisModel();
      if (!objects.Any()) return admModel;

      ISpeckleConverter converter = GetSCIAConverter();

      // Set context document from SCIA in the converter
      converter.SetContextDocument(admModel);

      // Convert Speckle Base objects to SCIA ADM
      converter.ConvertToNative(objects);
      report = converter.Report;

      return admModel;
    }

    public static Base ConvertToSpeckle(AnalysisModel admModel, out ProgressReport report)
    {
      ISpeckleConverter converter = GetSCIAConverter();
      Base speckleModel = converter.ConvertToSpeckle(admModel);

      report = converter.Report;

      return speckleModel;
    }
    #endregion


    #region ### SAF Import/Export ###
    private static string AppendTimeStampToPath(string fileName)
    {
      string newFileName = string.Concat(
              Path.GetFileNameWithoutExtension(fileName),
              DateTime.Now.ToString("_yyyyMMddHHmmssfff"),
              Path.GetExtension(fileName));
      return Path.Combine(Path.GetDirectoryName(fileName), newFileName);
    }

    public static void ExportToSAF(AnalysisModel model, string path)
    {
      // Rename the old file, as overwrite doesn't seem to work using the code below
      if (File.Exists(path))
      {
        var backupPath = AppendTimeStampToPath(path);
        File.Move(path, backupPath);
      }

      var bootstrapper = new ExcelModuleBootstrapper();
      using (IScopedServiceProvider scope = bootstrapper.CreateThreadedScope())
      {
        IExcelWriter excelWriter = scope.GetService<IExcelWriter>();
        // The ADM uses EPPLUS to manipulate Excel files, which needs read and writing access
        using (FileStream fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
          excelWriter.Write(fs, model);
        }
      }
    }

    public static AnalysisModel ImportFromSAF(string path)
    {
      var bootstrapper = new ExcelModuleBootstrapper();
      using (IScopedServiceProvider scope = bootstrapper.CreateThreadedScope())
      {
        IExcelReader excelReader = scope.GetService<IExcelReader>();
        using (FileStream fs = File.OpenRead(path))
        {
          ExcelReadResult result = excelReader.ReadWithInfo(fs, AnalysisModelImportValidity.AllowInvalid);
          AnalysisModel model = result.ImportResult.AnalysisModel;
          //model.Warnings;
          return model;
        }
      }
    }
    #endregion


    #region ### TIMER ###
    private static DateTime _startTime;

    public static void StartTimer()
    {
      _startTime = DateTime.Now;
      Console.WriteLine(" ~ Start timer at {0:HH:mm:ss.fff} ~", DateTime.Now);
    }

    public static void WriteTime()
    {
      var now = DateTime.Now;
      var span = now - _startTime;
      Console.WriteLine($" ~ Time elapsed at {now:HH:mm:ss.fff} = {span:c} ~");
    }
    #endregion


    #region ### SCIA integration ###
    private static void ReceiveStream()
    {
      // Get Speckle stream data and (if needed) open a WebSocket to listen to new commits
      var receivedObj = GetStreamLatestData().Result;

      ISpeckleConverter converter = GetSCIAConverter();
      List<Base> speckleObjects = FlattenCommitData(receivedObj, converter);

      Console.WriteLine($"Converting {speckleObjects.Count()} Base objects:");
      foreach (var item in speckleObjects)
      {
        var name = item["name"] ?? "";
        Console.WriteLine($" - {item} {name}");
      }
      // Open a SCIA connection and push the data to SCIA
      PushToSCIA(speckleObjects, converter).Wait();
    }

    /// <summary>
    /// Push Speckle object to active SCIA Engineer environment
    /// </summary>
    /// <param name="base"></param>
    /// <returns></returns>
    private static async Task PushToSCIA(List<Base> @base, ISpeckleConverter converter)
    {
      await InitSCIAProject();

      AnalysisModel admModel = new AnalysisModel();

      // Set context document from SCIA in the converter
      converter.SetContextDocument(admModel);

      // Convert Speckle Base objects to SCIA ADM
      converter.ConvertToNative(@base);

      Console.WriteLine("### CONVERSION LOG ###");
      Console.WriteLine(converter.Report.ConversionLogString);

      if (!admModel.Any()) throw new Exception("Analysis model conversion returns nothing");

      Structure apiModel = Project.Model;
      foreach (var obj in admModel)
      {
        Console.WriteLine($" - Creating {obj.GetType().Name} {obj.Name}");

        ResultOfPartialAddToAnalysisModel addResult = apiModel.CreateAdmObject(obj);
        if (addResult.PartialAddResult.Status != AdmChangeStatus.Ok)
        {
          throw HandleErrorResult(addResult);
        }
        else
        {
          // Console.WriteLine($" - Created {obj.GetType().Name} {addResult.PartialAddResult.Names}"); 
        }
      }

      apiModel.RefreshModel_ToSCIAEngineer();
      ContextModel = admModel;
    }

    /// <summary>
    /// Use SCIA.OpenAPI methods to add a beam to the model
    /// </summary>
    /// <param name="name"></param>
    /// <param name="node1"></param>
    /// <param name="node2"></param>
    /// <param name="profile1D"></param>
    private static void Add1DMember(string name, string node1, string node2, string profile1D)
    {
      Console.WriteLine($"Creating beam '{name}' with cross section '{profile1D}' between '{node1}' and '{node2}'");
      var model = Project.Model;
      try
      {
        model.RefreshModel_FromSCIAEngineer(); // Update the model from the SCIA (in case manual modifications have been made)
        var cssGuid = model.FindGuid(profile1D);
        var n1Guid = model.FindGuid(node1);
        var n2Guid = model.FindGuid(node2);
        var beam = new Beam(Guid.NewGuid(), name, cssGuid, new ApiGuidArr(new ApiGuid[] { n1Guid, n2Guid }));
        model.CreateBeam(beam);
        model.RefreshModel_ToSCIAEngineer();
      }
      catch (Exception e)
      {
        Console.WriteLine($"Can't create member: {e.Message}");
        return;
      }
    }

    /// <summary>
    /// Use SCIA.OpenAPI to calculate the active Project and initialize the ResultsAPI
    /// </summary>
    /// <returns></returns>
    private static ResultsAPI RunCalculation()
    {
      Project.RunCalculation();
      return Project.Model.InitializeResultsAPI();
    }


    #region SCIA Environment Setup
    private static async Task InitSCIAProject(string path = null)
    {
      if (SCIAenv == null)
      {
        await InitSCIAEnvironment();
      }

      SCIAenv.CloseAllProjects(SaveMode.SaveChangesNo);
      Project = OpenSCIAFile(SCIAenv, path);
    }

    /// <summary>
    /// Start a new SCIA Engineer environment
    /// </summary>
    /// <returns></returns>
    private static async Task InitSCIAEnvironment()
    {
      Console.WriteLine("Check : Closing existing environment");
      await CloseSCIAEnvironment();

      Console.WriteLine("Check : Closing existing SCIA processes");
      KillSCIAEngineerOrphanRuns();

      Console.WriteLine("Check : Initiating new environment");
      SCIAenv = new SCIA.OpenAPI.Environment(SCIA_APP_PATH, LOG_PATH, "1.0.0.0");  // path to the location of your installation and temp path for logs)

      Console.WriteLine("Check : Initiating SCIA app");
      //Run SCIA Engineer application and open an empty document
      if (!SCIAenv.RunSCIAEngineer(SCIA.OpenAPI.Environment.GuiMode.ShowWindowShow))
      {
        throw new Exception("Failed to open SCIA app");
      }
    }

    private static async Task CloseSCIAEnvironment()
    {
      SCIAenv?.CloseAllProjects(SaveMode.SaveChangesNo);
      SCIAenv?.Dispose();
    }

    private static void KillSCIAEngineerOrphanRuns()
    {
      string[] procNames = { "EsaStartupScreen", "Esa", "SciaEngineer", "EsaEngineeringReport", "SciaTools.AdmToAdm.HubClient", "SciaTools.AdmToAdm.HubServer" };
      foreach (string procName in procNames)
      {
        foreach (var process in Process.GetProcessesByName(procName))
        {
          process.Kill();
          Console.WriteLine($"Killing process: {procName}");
          System.Threading.Thread.Sleep(3000);
        }
      }
    }

    private static EsaProject OpenSCIAFile(SCIA.OpenAPI.Environment env, string path = null)
    {
      if (string.IsNullOrEmpty(path))
      {
        SciaFileGetter fileGetter = new SciaFileGetter();
        path = fileGetter.PrepareBasicEmptyFile(@"C:/TEMP/");  //path where the template file "template.esa" is created
        if (!File.Exists(path))
        {
          throw new InvalidOperationException("File from manifest resource is not created !");
        }
      }

      EsaProject project = env.OpenProject(path);
      if (project == null)
      {
        throw new InvalidOperationException("File from manifest resource is not opened !");
      }

      return project;
    }
    #endregion

    #region SCIA error handling
    private static Exception HandleErrorResult(ResultOfPartialAddToAnalysisModel addResult)
    {
      switch (addResult.PartialAddResult.Status)
      {
        case AdmChangeStatus.InvalidInput:
          throw new Exception(addResult.PartialAddResult.Warnings);
        case AdmChangeStatus.Error:
          throw new Exception(addResult.PartialAddResult.Errors);
        case AdmChangeStatus.NotDone:
          throw new Exception(addResult.PartialAddResult.Warnings);
      }
      if (addResult.PartialAddResult.Exception != null)
      {
        throw addResult.PartialAddResult.Exception;
      }
      throw new Exception("Unknown ADM Error");
    }
    #endregion

    #endregion


    #region ### SpeckleObjects ###

    #region Looking for Base objects in Speckle commit data
    /// <summary>
    /// Recurses through the commit object and flattens it. 
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="converter"></param>
    /// <returns></returns>
    private static List<Base> FlattenCommitData(object obj, ISpeckleConverter converter)
    {
      List<Base> objects = new List<Base>();

      if (obj is Base @base)
      {
        if (converter.CanConvertToNative(@base))
        {
          // If the Base object can be converted to Native, the conversion of all nested Base objects
          // is managed within the Converter assembly.
          objects.Add(@base);
          return objects;
        }
        else
        {
          /* ToDo: original method from RevitConnector uses @base.GetDynamicMembers() here, to exclude hard-typed properties
           * If however the AnalysisModel cannot be converted, this will then skip its elements, nodes, ... properties
           * Q: Is this the desired behaviour? */
          foreach (var prop in @base.GetMembers().Keys)
          {
            objects.AddRange(FlattenCommitData(@base[prop], converter));
          }
          return objects;
        }
      }

      if (obj is IList list)
      {
        foreach (var listObj in list)
        {
          objects.AddRange(FlattenCommitData(listObj, converter));
        }
        return objects;
      }

      if (obj is IDictionary dict)
      {
        foreach (DictionaryEntry kvp in dict)
        {
          objects.AddRange(FlattenCommitData(kvp.Value, converter));
        }
        return objects;
      }

      return objects;
    }

    public static Base FindFirstSpeckleObjectOfType(object obj, string speckleType)
    {
      string typeProp = "speckle_type";

      if (obj is Base @base)
      {
        if (speckleType.Equals(@base[typeProp]))
        {
          return @base;
        }
        foreach (var member in @base.GetDynamicMembers())
        {
          var result = FindFirstSpeckleObjectOfType(@base[member], speckleType);
          if (result != null)
          {
            return result;
          }
        }
      }
      else if (obj is IList list)
      {
        foreach (var item in list)
        {
          var result = FindFirstSpeckleObjectOfType(item, speckleType);
          if (result != null)
          {
            return result;
          }
        }
      }
      return null;
    }
    #endregion

    #region Printing objects
    private static void PrintObjectSerialized(Base obj)
    {
      // View full object as json
      Console.WriteLine(Operations.Serialize(obj));
    }

    private static void PrintObjectRecursive(object obj, int level = 0)
    {
      string padding = "".PadLeft(level * 2);
      if (obj is Base baseObj)
      {
        foreach (var m in baseObj.GetMembers())
        {
          string msg = $"{padding}- Member {m.Key} = ";

          if (!(m.Value is Base) && !(m.Value is List<object>) && !(m.Value is List<Base>))
          {
            string valueString = $"{m.Value}";
            if (!string.IsNullOrEmpty(valueString))
            {
              msg += valueString;
              Console.WriteLine(msg);
            }
            continue;
          }

          Console.WriteLine(msg);
          PrintObjectRecursive(m.Value, level + 1);
        }
      }
      else if (obj is List<Base> baseList)
      {
        string msg = $"{padding}- List of {baseList.Count} Base item(s)";
        Console.WriteLine(msg);

        foreach (var item in baseList)
        {
          PrintObjectRecursive(item, level + 1);
        }
      }
      else if (obj is List<object> list)
      {
        string msg = $"{padding}- List of {list.Count} item(s)";
        Console.WriteLine(msg);

        foreach (var item in list)
        {
          PrintObjectRecursive(item, level + 1);
        }
      }
      else
      {
        Console.WriteLine($"{padding}- {obj}");
      }
    }
    #endregion

    #endregion


    #region ### SpeckleServer Connection ###

    private static async Task InitSpeckleClient(StreamWrapper wrapper, bool subscribe)
    {
      SpeckleClient?.Dispose();

      Account account = await wrapper.GetAccount();
      SpeckleClient = new Client(account);

      Console.WriteLine($"Speckle Client launched for {account}");

      if (subscribe)
      {
        // Subscribe to the Commit Created event
        SpeckleClient.SubscribeCommitCreated(wrapper.StreamId);
        SpeckleClient.OnCommitCreated += SpeckleClient_OnCommitCreated;
      }
    }

    private static void SpeckleClient_OnCommitCreated(object sender, CommitInfo e)
    {
      // Break if wrapper is branch type and branch name is not equal.
      if (SpeckleStreamWrapper.Type == StreamWrapperType.Branch && e.branchName != SpeckleStreamWrapper.BranchName) return;
      SpeckleStreamWrapper.CommitId = e.id;
      ReceiveStream();
    }

    /// <summary>
    /// Get Speckle data from the StreamWrapper stored in memory (assumes there is only one account locally registered for the given serverDomain).
    /// The data of the latest commit is returned, and a listener is added to check for any new commits.
    /// </summary>
    /// <returns></returns>
    private static async Task<Base> GetStreamLatestData()
    {
      if (SpeckleClient == null) await InitSpeckleClient(SpeckleStreamWrapper, OPEN_SOCKET);

      string objectId;
      switch (SpeckleStreamWrapper.Type)
      {
        case StreamWrapperType.Branch:
          Branch branch = await SpeckleClient.BranchGet(SpeckleStreamWrapper.StreamId, SpeckleStreamWrapper.BranchName, 1);
          Commit latestCommit = branch.commits.items[0];
          SpeckleStreamWrapper.CommitId = latestCommit.id;
          objectId = latestCommit.referencedObject;
          break;

        case StreamWrapperType.Commit:
          Commit commit = await SpeckleClient.CommitGet(SpeckleStreamWrapper.StreamId, SpeckleStreamWrapper.CommitId);
          objectId = commit.referencedObject;
          break;

        case StreamWrapperType.Object:
          objectId = SpeckleStreamWrapper.ObjectId;
          break;

        default:
          throw new NotImplementedException();
      }

      Console.WriteLine($"Collecting Speckle object '{objectId}' from" +
          $"\n - stream '{SpeckleStreamWrapper.StreamId}', " +
          $"\n - branch '{SpeckleStreamWrapper.BranchName}', " +
          $"\n - commit '{SpeckleStreamWrapper.CommitId}'");

      return await Operations.Receive(objectId);
    }

    private static void PrintAccounts()
    {
      Console.WriteLine($"Getting available accounts");

      foreach (var acc in AccountManager.GetAccounts())
      {
        Console.WriteLine($" - {acc}, id = {acc.id}, user = {acc.userInfo.name}, default = {acc.isDefault}");
      }
    }

    private static Account GetAccount()
    {
      return AccountManager.GetDefaultAccount();
    }

    private static Account GetAccount(string serverUrl = null, string userId = null, string userName = null, string userEmail = null)
    {
      var accounts = serverUrl != null ? AccountManager.GetAccounts(serverUrl) : AccountManager.GetAccounts();
      return accounts.FirstOrDefault(acc =>
          (userId == null || acc.userInfo.id == userId) &&
          (userName == null || acc.userInfo.name == userName) &&
          (userEmail == null || acc.userInfo.email == userEmail));
    }
    #endregion


    #region ### KitManager ###
    private static ISpeckleConverter GetSCIAConverter()
    {
      string appName = SCIA_CONVERTER_APP_NAME;

      var kit = KitManager.GetKitsWithConvertersForApp(appName).FirstOrDefault();
      if (kit == null)
      {
        throw new Exception($"Can't find {appName} converter");
      }

      var converter = kit.LoadConverter(appName);
      if (converter == null)
      {
        throw new Exception($"Can't load {appName} converter");
      }
      Console.WriteLine($"Loaded {appName} converter from kit {kit.Name}");
      return converter;
    }

    static void PrintKits()
    {
      Console.WriteLine($"Getting available kits in {KitManager.KitsFolder}");

      foreach (var kit in KitManager.Kits)
      {
        Console.WriteLine($" - {kit.Name}, {kit.Author}");
        var converters = kit.Converters;
        foreach (var c in converters)
        {
          Console.WriteLine($"  -> {c}");
        }
      }
    }
    #endregion


    #region ### Assembly resolve ###
    /// <summary>
    /// Assembly resolve method has to be call here.
    /// Only needs to be performed once! Otherwise, we'll have duplicate events...
    /// </summary>
    private static void SciaOpenApiAssemblyResolve()
    {
      AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
      {
        string dllName = args.Name.Substring(0, args.Name.IndexOf(",")) + ".dll";
        string dllFullPath = Path.Combine(SCIA_APP_PATH, dllName);
        if (!File.Exists(dllFullPath))
        {
            // look into OpenAPI_dll subfolder
            dllFullPath = Path.Combine(SCIA_APP_PATH, "OpenAPI_dll", dllName);
          if (!File.Exists(dllFullPath))
          {
            return null;
          }
        }
        return Assembly.LoadFrom(dllFullPath);
      };
    }
    #endregion
  }
}
