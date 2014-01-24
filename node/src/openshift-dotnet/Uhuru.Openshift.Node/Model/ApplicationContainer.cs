﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Uhuru.Openshift.Runtime.Config;
using Uhuru.Openshift.Runtime.Model;
using Uhuru.Openshift.Runtime.Utils;
using Uhuru.Openshift.Utilities;
using Uhuru.Openshift.Common.Models;
using Uhuru.Openshift.Common.Utils;

namespace Uhuru.Openshift.Runtime
{
    public partial class ApplicationContainer
    {
        public const double PARALLEL_CONCURRENCY_RATIO = 0.2;
        public const int MAX_THREADS = 8;
        public const string RESULT_SUCCESS = "success";
        public const string RESULT_FAILURE = "failure";

        public string Uuid { get; set; }
        public string ApplicationUuid { get; set; }
        public string ContainerName { get; set; }
        public string ApplicationName { get; set; }
        public string Namespace { get; set; }
        public string BaseDir { get; set; }

        public object QuotaBlocks { get; set; }
        public object QuotaFiles { get; set; }
        
        public string ContainerDir 
        { 
            get 
            { 
                return Path.Combine(NodeConfig.Values["GEAR_BASE_DIR"], this.Uuid);
            } 
        }

        public GearRegistry GearRegist
        {
            get
            {
                if (this.gearRegistry == null && this.Cartridge.WebProxy() != null)
                {
                    this.gearRegistry = new GearRegistry(this);
                }
                return this.gearRegistry;
            }
        }
                
        public CartridgeModel Cartridge { get; set; }
        public Hourglass GetHourglass { get { return this.hourglass; } }
        public ApplicationState State { get; set; }

        ContainerPlugin containerPlugin;
        NodeConfig config;
        private Hourglass hourglass;
        private GearRegistry gearRegistry;

        public ApplicationContainer(string applicationUuid, string containerUuid, string userId, string applicationName,
            string containerName, string namespaceName, object quotaBlocks, object quotaFiles, Hourglass hourglass)
        {
            this.config = NodeConfig.Values;
            this.Uuid = containerUuid;
            this.ApplicationUuid = applicationUuid;
            this.ApplicationName = applicationName;
            this.ContainerName = containerName;
            this.Namespace = namespaceName;
            this.QuotaBlocks = quotaBlocks;
            this.QuotaFiles = quotaFiles;
            this.State = new ApplicationState(this);            
            this.hourglass = hourglass ?? new Hourglass(3600);
            this.BaseDir = this.config["GEAR_BASE_DIR"];
            this.containerPlugin = new ContainerPlugin(this);
            this.Cartridge = new CartridgeModel(this, this.State, this.hourglass);
        }

        public string Create(string secretToken = null)
        {            
            containerPlugin.Create();
            return string.Empty;
        }

        public string Destroy()
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine(this.Cartridge.Destroy());
            output.AppendLine(this.RemoveSshdUser());
            output.AppendLine(this.containerPlugin.Destroy());            
            return output.ToString();
        }

        public string KillProcs(dynamic options = null)
        {
            // TODO need to kill all user processes. stopping gear for now
            return this.StopGear(options);
        }

        public string Configure(string cartName, string templateGitUrl, string manifest)        
        {
            return Cartridge.Configure(cartName, templateGitUrl, manifest);
        }

        public string ConnectorExecute(string cartName, string hookName, string publishingCartName, string connectionType, string inputArgs)
        {
            return Cartridge.ConnectorExecute(cartName, hookName, publishingCartName, connectionType, inputArgs);
        }

        public string Start(string cartName, dynamic options = null)
        {
            if (options == null)
            {
                options = new Dictionary<string,object>();
            }
            return this.Cartridge.StartCartridge("start", cartName, options);
        }

        public string Stop(string cartName, dynamic options = null)
        {
            if (options == null)
            {
                options = new Dictionary<string, object>();
            }
            return this.Cartridge.StopCartridge(cartName, true, options);
        }

        public string RemoveSshdUser()
        {
            string output = "";
            string binLocation = Path.GetDirectoryName(this.GetType().Assembly.Location);
            string script = Path.GetFullPath(Path.Combine(binLocation, @"powershell\Tools\sshd\remove-sshd-user.ps1"));

            ProcessStartInfo pi = new ProcessStartInfo();
            pi.UseShellExecute = false;
            pi.RedirectStandardError = true;
            pi.RedirectStandardOutput = true; pi.FileName = "powershell.exe";

            pi.Arguments = string.Format(
@"-ExecutionPolicy Bypass -InputFormat None -noninteractive -file {0} -targetDirectory {2} -user {1} -windowsUser administrator -userHomeDir {3} -userShell {4}",
                script,
                this.ApplicationUuid,
                NodeConfig.Values["SSHD_BASE_DIR"],
                this.ContainerDir,
                NodeConfig.Values["GEAR_SHELL"]);

            Process p = Process.Start(pi);
            p.WaitForExit(60000);
            output += p.StandardError.ReadToEnd();
            output += p.StandardOutput.ReadToEnd();

            return output;
        }

        public string AddSshKey(string sshKey, string keyType, string comment)
        {
            string output = "";

            string key = string.Format("{0} {1} {2}", keyType, sshKey, comment);

            string binLocation = Path.GetDirectoryName(this.GetType().Assembly.Location);
            string configureScript = Path.GetFullPath(Path.Combine(binLocation, @"powershell\Tools\sshd\configure-sshd.ps1"));
            string addKeyScript = Path.GetFullPath(Path.Combine(binLocation, @"powershell\Tools\sshd\add-key.ps1"));

            ProcessStartInfo pi = new ProcessStartInfo();            
            pi.UseShellExecute = false;
            pi.RedirectStandardError = true;
            pi.RedirectStandardOutput = true; pi.FileName = "powershell.exe";
            
            pi.Arguments = string.Format(
@"-ExecutionPolicy Bypass -InputFormat None -noninteractive -file {0} -targetDirectory {2} -user {1} -windowsUser {5} -userHomeDir {3} -userShell {4}", 
                configureScript, 
                this.Uuid, 
                NodeConfig.Values["SSHD_BASE_DIR"], 
                this.ContainerDir,
                NodeConfig.Values["GEAR_SHELL"],
                Environment.UserName);

            Process p = Process.Start(pi);
            p.WaitForExit(60000);
            output += this.Uuid;
            output += p.StandardError.ReadToEnd();
            output += p.StandardOutput.ReadToEnd();

            pi.Arguments = string.Format(@"-ExecutionPolicy Bypass -InputFormat None -noninteractive -file {0} -targetDirectory {2} -windowsUser {3} -key ""{1}""", 
                addKeyScript, 
                key, 
                NodeConfig.Values["SSHD_BASE_DIR"],
                Environment.UserName);
            p = Process.Start(pi);
            p.WaitForExit(60000);
            output += p.StandardError.ReadToEnd();
            output += p.StandardOutput.ReadToEnd();           

            return output;
        }

        public void Distribute(dynamic options)
        {

        }

        private DateTime CreateDeploymentDir()
        {
            DateTime deploymentdateTime = DateTime.Now;

            string fullPath = Path.Combine(this.ContainerDir, "app-deployments", deploymentdateTime.ToString("yyyy-MM-dd_HH-mm-s"));
            Directory.CreateDirectory(Path.Combine(fullPath, "repo"));
            Directory.CreateDirectory(Path.Combine(fullPath, "dependencies"));
            Directory.CreateDirectory(Path.Combine(fullPath, "build-depedencies"));
            SetRWPermissions(fullPath);
            PruneDeployments();
            return deploymentdateTime;
        }

        private void PruneDeployments()
        {
            // TODO implement this!
        }



        private string StartGear(dynamic options)
        {
            return this.Cartridge.StartGear(options);
        }

        public void SetRWPermissions(string filename)
        {

        }

        public string StopGear(dynamic options)
        {
            return this.Cartridge.StopGear(options);
        }

        public string Restart(string cartName, dynamic options)
        {
            WithGearRotation(options,
                (GearRotationCallback)delegate(object targetGear, Dictionary<string, string> localGearEnv, dynamic opts)
                {
                    RestartGear(targetGear, localGearEnv, cartName, opts);
                });


            return string.Empty;
        }

        public void SetAutoDeploy(bool autoDeploy)
        {
            AddEnvVar("AUTO_DEPLOY", autoDeploy.ToString().ToLower(), true);
        }

        public void SetKeepDeployments(int keepDeployments)
        {
            AddEnvVar("KEEP_DEPLOYMENTS", keepDeployments.ToString(), true);
            // TODO Clean up any deployments over the limit
        }

        public void SetDeploymentBranch(string deploymentBranch)
        {
            AddEnvVar("DEPLOYMENT_BRANCH", deploymentBranch, true);
        }

        public void SetDeploymentType(string deploymentType)
        {
            AddEnvVar("DEPLOYMENT_TYPE", deploymentType, true);
        }

        public delegate RubyHash GearRotationCallback(object targetGear, Dictionary<string, string> localGearEnv, RubyHash options);
        public dynamic WithGearRotation(dynamic options, GearRotationCallback action)
        {
            dynamic localGearEnv = Environ.ForGear(this.ContainerDir);
            Manifest proxyCart = this.Cartridge.WebProxy();
            List<object> gears = new List<object>();
            if (options.ContainsKey("all") && proxyCart != null)
            {
                if ((bool)options["all"])
                {
                    gears = this.GearRegist.Entries["web"].Keys.ToList<object>();
                }
                else if (options.ContainsKey("gears"))
                {
                    List<string> g = (List<string>)options["gears"];
                    gears = this.GearRegist.Entries["web"].Keys.Where(e => g.Contains(e)).ToList<object>();
                }
                else
                {
                    try
                    {
                        gears.Add(this.GearRegist.Entries["web"][this.Uuid]);
                    }
                    catch
                    {
                        gears.Add(this.Uuid);
                    }
                }
            }
            else
            {
                gears.Add(this.Uuid);
            }

            double parallelConcurrentRatio = PARALLEL_CONCURRENCY_RATIO;
            if (options.ContainsKey("parallel_concurrency_ratio"))
            {
                parallelConcurrentRatio = (double)options["parallel_concurrency_ratio"];
            }

            int batchSize = CalculateBatchSize(gears.Count, parallelConcurrentRatio);

            int threads = Math.Max(batchSize, MAX_THREADS);

            dynamic result = new List<object>();

            // need to parallelize
            foreach (var targetGear in gears)
            {
                result.Add(RotateAndYield(targetGear, localGearEnv, options, action));
            }

            return result;
        }

        public RubyHash RotateAndYield(object targetGear, Dictionary<string, string> localGearEnv, RubyHash options, GearRotationCallback action)
        {
            RubyHash result = new RubyHash()
            {
                { "status", "RESULT_FAILURE" },
                { "messages", new List<string>() },
                { "errors", new List<string>() }
            };

            string proxyCart = options["proxy_cart"];

            string targetGearUuid = targetGear is string ? (string)targetGear : ((GearRegistry.Entry)targetGear).Uuid;

            // TODO: vladi: check if this condition also needs boolean verification on the value in the hash
            if (options["init"] == null && options["rotate"] != false && options["hot_deploy"] != true)
            {
                result["messages"].Add("Rotating out gear in proxies");

                RubyHash rotateOutResults = this.UpdateProxyStatus(new RubyHash()
                    {
                        { "action", "disable" },
                        { "gear_uuid", targetGearUuid },
                        { "cartridge", proxyCart }
                    });

                result["rotate_out_results"] = rotateOutResults;

                if (rotateOutResults["errors"] != "RESULT_SUCCESS")
                {
                    result["errors"].Add("Rotating out gear in proxies failed.");
                    return result;
                }
            }

            RubyHash yieldResult = action(targetGear, localGearEnv, options);

            dynamic yieldStatus = yieldResult.Delete("status");
            dynamic yieldMessages = yieldResult.Delete("messages");
            dynamic yieldErrors = yieldResult.Delete("errors");

            result["messages"].AddRange(yieldMessages);
            result["errors"].AddRange(yieldErrors);

            result = result.Merge(yieldResult);

            if (yieldStatus != "RESULT_SUCCESS")
            {
                return result;
            }

            if (options["init"] == null && options["rotate"] != false && options["hot_deploy"] != true)
            {
                result["messages"].Add("Rotating in gear in proxies");

                RubyHash rotateInResults = this.UpdateProxyStatus(new RubyHash()
                {
                    { "action", "enable" },
                    { "gear_uuid", targetGearUuid },
                    { "cartridge", proxyCart }
                });

                result["rotate_in_results"] = rotateInResults;

                if (rotateInResults["status"] != "RESULT_SUCCESS")
                {
                    result["errors"].Add("Rotating in gear in proxies failed");
                    return result;
                }
            }

            result["status"] = "RESULT_SUCCESS";

            return result;
        }

        public RubyHash UpdateProxyStatus(RubyHash options)
        {
            string action = options["action"];

            if (action != "enable" && action != "disable")
            {
                throw new ArgumentException("action must either be :enable or :disable");
            }

            if (options["gear_uuid"] == null)
            {
                throw new ArgumentException("gear_uuid is required");
            }

            string gearUuid = options["gear_uuid"];

            Manifest cartridge;

            if (options["cartridge"] == null)
            {
                cartridge = this.Cartridge.WebProxy();
            }
            else
            {
                cartridge = options["cartridge"];
            }

            if (cartridge == null)
            {
                throw new ArgumentException("Unable to update proxy status - no proxy cartridge found");
            }

            dynamic persist = options["persist"];

            Dictionary<string, string> gearEnv = Environ.ForGear(this.ContainerDir);

            RubyHash result = new RubyHash() {
                { "status", "RESULT_SUCCESS" },
                { "target_gear_uuid", gearUuid },
                { "proxy_results", new RubyHash() }
            };

            RubyHash gearResult = new RubyHash();

            if (gearEnv["OPENSHIFT_APP_DNS"] != gearEnv["OPENSHIFT_GEAR_DNS"])
            {
                gearResult = this.UpdateLocalProxyStatus(new RubyHash(){
                    { "cartridge", cartridge },
                    { "action", action },
                    { "proxy_gear", this.Uuid },
                    { "target_gear", gearUuid },
                    { "persist", persist }
                });

                result["proxy_results"][this.Uuid] = gearResult;
            }
            else
            {
                // only update the other proxies if we're the currently elected proxy
                // TODO the way we determine this needs to change so gears other than
                // the initial proxy gear can be elected
                GearRegistry.Entry[] proxyEntries = this.gearRegistry.Entries["proxy"].Values.ToArray();

                // TODO: vladi: Make this parallel
                RubyHash[] parallelResults = proxyEntries.Select(entry =>
                    this.UpdateRemoteProxyStatus(new RubyHash()
                    {
                        { "current_gear", this.Uuid },
                        { "proxy_gear", entry },
                        { "target_gear", gearUuid },
                        { "cartridge", cartridge },
                        { "action", action },
                        { "persist", persist },
                        { "gear_env", gearEnv }
                    })).ToArray();
                
                foreach (RubyHash parallelResult in parallelResults)
                {
                    if (parallelResult.ContainsKey("proxy_results"))
                    {
                        result["proxy_results"] = result["proxy_results"].Merge(parallelResult["proxy_results"]);
                    }
                    else
                    {
                        result["proxy_results"][parallelResult["proxy_gear_uuid"]] = parallelResult;
                    }
                }
            }

            // if any results failed, consider the overall operation a failure
            foreach (RubyHash proxyResult in result["proxy_results"].Values)
            {
                if (proxyResult["status"] != "RESULT_SUCCESS")
                {
                    result["status"] = "RESULT_FAILURE";
                }
            }

            return result;
        }

        public RubyHash UpdateLocalProxyStatus(RubyHash args)
        {
            RubyHash result = new RubyHash();

            object cartridge = args["cartridge"];
            object action = args["action"];
            object targetGear =args["target_gear"];
            object persist = args["persist"];

            try
            {
                RubyHash output = this.UpdateProxyStatusForGear(new RubyHash(){
                    { "cartridge", cartridge },
                    { "action", action },
                    { "gear_uuid", targetGear },
                    { "persist", persist }
                });

                result = new RubyHash()
                {
                    { "status", "RESULT_SUCCESS" },
                    { "proxy_gear_uuid", this.Uuid },
                    { "target_gear_uuid", targetGear },
                    { "messages", new List<string>() },
                    { "errors", new List<string>() }
                };
            }
            catch (Exception ex)
            {
                result = new RubyHash()
                {
                    { "status", "RESULT_FAILURE" },
                    { "proxy_gear_uuid", this.Uuid },
                    { "target_gear_uuid", targetGear },
                    { "messages", new List<string>() },
                    { "errors", new List<string>() { string.Format("An exception occured updating the proxy status: {0}\n{1}", ex.Message, ex.StackTrace) } }
                };
            }

            return result;
        }

        public dynamic UpdateProxyStatusForGear(RubyHash options)
        {
            string action = options["action"];

            if (action != "enable" && action != "disable")
            {
                new ArgumentException("action must either be :enable or :disable");
            }

            string gearUuid = options["gear_uuid"];

            if (gearUuid == null)
            {
                new ArgumentException("gear_uuid is required");
            }

            Manifest cartridge = options["cartridge"] ?? this.Cartridge.WebProxy();

            if (cartridge == null)
            {
                throw new ArgumentNullException("Unable to update proxy status - no proxy cartridge found");
            }

            bool persist = options["persist"] != null && options["persist"] == true;
            string control = string.Format("{0}-server", action);

            List<string> args = new List<string>();

            if (persist)
            {
                args.Add("persist");
            }

            args.Add(gearUuid);

            this.Cartridge.DoControl(
                control, cartridge, string.Join(" ", args), new Dictionary<string, object>()
                {
                    { "pre_action_hooks_enabled", false },
                    { "post_action_hooks_enabled", false }
                });
            //args << 'persist' if persist
            //args << gear_uuid

            //@cartridge_model.do_control(control,
            //                            cartridge,
            //                            args: args.join(' '),
            //                            pre_action_hooks_enabled:  false,
            //                            post_action_hooks_enabled: false)
            return null;
        }

        public RubyHash UpdateRemoteProxyStatus(RubyHash args)
        {
            RubyHash result = new RubyHash();
            //current_gear = args[:current_gear]
            //proxy_gear = args[:proxy_gear]
            //target_gear = args[:target_gear]
            //cartridge = args[:cartridge]
            //action = args[:action]
            //persist = args[:persist]
            //gear_env = args[:gear_env]

            //if current_gear == proxy_gear.uuid
            //  # self, no need to ssh
            //  return update_local_proxy_status(cartridge: cartridge, action: action, target_gear: target_gear, persist: persist)
            //end

            //direction = if :enable == action
            //  'in'
            //else
            //  'out'
            //end

            //persist_option = if persist
            //  '--persist'
            //else
            //  ''
            //end

            //url = "#{proxy_gear.uuid}@#{proxy_gear.proxy_hostname}"

            //command = "/usr/bin/oo-ssh #{url} gear rotate-#{direction} --gear #{target_gear} #{persist_option} --cart #{cartridge.name}-#{cartridge.version} --as-json"

            //begin
            //  out, err, rc = run_in_container_context(command,
            //                                          env: gear_env,
            //                                          expected_exitstatus: 0)

            //  raise "No result JSON was received from the remote proxy update call" if out.nil? || out.empty?

            //  result = HashWithIndifferentAccess.new(JSON.load(out))

            //  raise "Invalid result JSON received from remote proxy update call: #{result.inspect}" unless result.has_key?(:status)
            //rescue => e
            //  result = {
            //    status: RESULT_FAILURE,
            //    proxy_gear_uuid: proxy_gear.uuid,
            //    messages: [],
            //    errors: ["An exception occured updating the proxy status: #{e.message}\n#{e.backtrace.join("\n")}"]
            //  }
            //end

            return result;
        }

        public string RestartGear(object targetGear, Dictionary<string, string> localGearEnv, string cartName, dynamic options)
        {
            return this.Cartridge.StartCartridge("restart", cartName, options);
        }

        public string Reload(string cartName)
        {
            if (string.Equals(this.State.Value(), Runtime.State.STARTED.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return this.Cartridge.DoControl("reload", cartName);
            }
            else
            {
                return this.Cartridge.DoControl("force-reload", cartName);
            }
        }

        public string RunProcessInContainerContext(string gearDirectory, string cmd)
        {
            StringBuilder output = new StringBuilder();

            ProcessStartInfo pi = new ProcessStartInfo();
            pi.EnvironmentVariables["PATH"] = Environment.GetEnvironmentVariable("PATH") + ";" + Path.Combine(NodeConfig.Values["SSHD_BASE_DIR"], "bin");
            pi.EnvironmentVariables["HOME"] = this.ContainerDir;
            pi.UseShellExecute = false;
            pi.CreateNoWindow = true;
            pi.RedirectStandardError = true;
            pi.RedirectStandardOutput = true;
            pi.WorkingDirectory = gearDirectory;

            pi.FileName = Path.Combine(Path.GetDirectoryName(typeof(ApplicationContainer).Assembly.Location), "oo-trap-user.exe");
            pi.Arguments = "-c \"" + cmd.Replace('\\','/') + "\"";
            
            Process p = new Process();
            p.StartInfo = pi;

            using (AutoResetEvent outputWaitHandle = new AutoResetEvent(false))
            using (AutoResetEvent errorWaitHandle = new AutoResetEvent(false))
            {
                p.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        outputWaitHandle.Set();
                    }
                    else
                    {
                        output.AppendLine(e.Data);
                    }
                };
                p.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        errorWaitHandle.Set();
                    }
                    else
                    {
                        output.AppendLine(e.Data);
                    }
                };

                p.Start();

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (p.WaitForExit(30000) &&
                    outputWaitHandle.WaitOne(30000) &&
                    errorWaitHandle.WaitOne(30000))
                {
                    // Process completed. Check process.ExitCode here.
                }
                else
                {
                    // Timed out.
                }
            }
            return output.ToString();
        }

        internal void SetRoPermissions(string hooks)
        {
        }

        internal void InitializeHomedir(string baseDir, string homeDir)
        {
            Directory.CreateDirectory(Path.Combine(homeDir, ".tmp"));
            Directory.CreateDirectory(Path.Combine(homeDir, ".sandbox"));
            
            string sandboxUuidDir = Path.Combine(homeDir, ".sandbox", this.Uuid);
            Directory.CreateDirectory(sandboxUuidDir);
            SetRWPermissions(sandboxUuidDir);

            string envDir = Path.Combine(homeDir, ".env");
            Directory.CreateDirectory(envDir);
            SetRoPermissions(envDir);

            string userEnvDir = Path.Combine(homeDir, ".env", "user_vars");
            Directory.CreateDirectory(userEnvDir);
            SetRoPermissions(userEnvDir);

            string sshDir = Path.Combine(homeDir, ".ssh");
            Directory.CreateDirectory(sshDir);
            SetRoPermissions(sshDir);

            string gearDir = Path.Combine(homeDir, this.ContainerName);
            string gearAppDir = Path.Combine(homeDir, "app-root");

            AddEnvVar("APP_DNS", string.Format("{0}-{1}.{2}", this.ApplicationName, this.Namespace, this.config["CLOUD_DOMAIN"]), true);
            AddEnvVar("APP_NAME", this.ApplicationName, true);
            AddEnvVar("APP_UUID", this.ApplicationUuid, true);
            
            string dataDir = Path.Combine(gearAppDir, "data");
            Directory.CreateDirectory(dataDir);
            AddEnvVar("DATA_DIR", dataDir, true);

            string deploymentsDir = Path.Combine(homeDir, "app-deployments");
            Directory.CreateDirectory(deploymentsDir);
            AddEnvVar("DEPLOYMENTS_DIR", deploymentsDir, true);
            Directory.CreateDirectory(Path.Combine(deploymentsDir, "by-id"));

            CreateDeploymentDir();

            AddEnvVar("GEAR_DNS", string.Format("{0}-{1}.{2}", this.ContainerName, this.Namespace, this.config["CLOUD_DOMAIN"]), true);
            AddEnvVar("GEAR_NAME", this.ContainerName, true);
            AddEnvVar("GEAR_UUID", this.Uuid, true);
            AddEnvVar("HOMEDIR", homeDir, true);
            AddEnvVar("HOME", homeDir, false);
            AddEnvVar("NAMESPACE", this.Namespace, true);

            string repoDir = Path.Combine(gearAppDir, "runtime", "repo");
            AddEnvVar("REPO_DIR", repoDir, true);
            Directory.CreateDirectory(repoDir);

            this.State.Value(Runtime.State.NEW);
        }

        public void AddEnvVar(string key, string value, bool prefixCloudName)
        {
            string envDir = Path.Combine(this.ContainerDir, ".env");
            if (prefixCloudName)
            {
                key = string.Format("OPENSHIFT_{0}", key);
            }
            string fileName = Path.Combine(envDir, key);
            File.WriteAllText(fileName, value);
            SetRoPermissions(fileName);
        }
        
        public void AddEnvVar(string key, string value)
        {
            AddEnvVar(key, value, false);
        }

        public string ForceStop(Dictionary<string, object> options = null)
        {
            this.State.Value(Uhuru.Openshift.Runtime.State.STOPPED);
            return this.containerPlugin.Stop();
        }

        private int CalculateBatchSize(int count, double ratio)
        {
            return (int)(Math.Max(1 / ratio, count) * ratio);
        }

        public string Tidy()
        {
            StringBuilder output = new StringBuilder();

            Dictionary<string, string> env = Environ.ForGear(this.ContainerDir);
            
            string gearDir = env["OPENSHIFT_HOMEDIR"];
            string appName = env["OPENSHIFT_APP_NAME"];

            string gearRepoDir = Path.Combine(gearDir, "git", string.Format("{0}.git", appName));
            string gearTmpDir = Path.Combine(gearDir, ".tmp");

            output.Append(StopGear(new Dictionary<string, object>() { { "user_initiated", false } }));
            try
            {
                GearLevelTidyTmp(gearTmpDir);
                output.AppendLine(this.Cartridge.Tidy());
                output.AppendLine(GearLevelTidyGit(gearRepoDir));
            }
            catch (Exception ex)
            {
                output.AppendLine(ex.ToString());
            }
            finally
            {
                StartGear(new Dictionary<string, object>() { { "user_initiated", false } });
            }
            return output.ToString();
        }

        private void GearLevelTidyTmp(string gearTmpDir)
        {
            DirectoryUtil.EmptyDirectory(gearTmpDir);
        }

        private string GearLevelTidyGit(string gearRepoDir)
        {
            StringBuilder output = new StringBuilder();
            string gitPath = Path.Combine(NodeConfig.Values["SSHD_BASE_DIR"], @"bin\git.exe");
            string cmd = string.Format("{0} prune", gitPath);
            output.AppendLine(RunProcessInContainerContext(gearRepoDir, cmd));

            cmd = string.Format("{0} gc --aggressive", gitPath);
            output.AppendLine(RunProcessInContainerContext(gearRepoDir, cmd));
            return output.ToString();
        }

        private string StoppedStatusAttr()
        {
            if (this.State.Value() == Uhuru.Openshift.Runtime.State.STARTED.ToString() || this.Cartridge.StopLockExists)
            {
                return "ATTR: status=ALREADY_STOPPED" + Environment.NewLine;
            }
            else
            {
                if (this.State.Value() == Uhuru.Openshift.Runtime.State.IDLE.ToString())
                {
                    return "ATTR: status=ALREADY_IDLED" + Environment.NewLine;
                }
                else
                {
                    return string.Empty;
                }
            }
        }
    }
}
