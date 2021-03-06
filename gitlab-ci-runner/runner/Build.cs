﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using gitlab_ci_runner.helper.json;

namespace gitlab_ci_runner.runner
{
    class Build
    {
        /// <summary>
        /// Build completed?
        /// Build internal!
        /// </summary>
        private bool __completed = false;

        /// <summary>
        /// Build completed?
        /// </summary>
        public bool completed
        {
            get
            {
                return __completed;
            }
        }

        /// <summary>
        /// Command output
        /// Build internal!
        /// </summary>
        private static LinkedList<string> __output = new LinkedList<string>();

        /// <summary>
        /// Command output
        /// </summary>
        public string output
        {
            get
            {
                string sOut = "";
                foreach (string line in __output)
                {
                    sOut += line + "\n";
                }
				return sOut;
            }
        }

        /// <summary>
        /// Projects Directory
        /// </summary>
        private string sProjectsDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\projects";

        /// <summary>
        /// Project Directory
        /// </summary>
        private string sProjectDir;

        /// <summary>
        /// Build Infos
        /// </summary>
        public BuildInfo buildInfo;

        /// <summary>
        /// Command list
        /// </summary>
        private LinkedList<string> commands;

        /// <summary>
        /// Execution State
        /// </summary>
        public State state = State.WAITING;

        /// <summary>
        /// Command Timeout
        /// </summary>
        public int iTimeout
        {
            get
            {
                return this.buildInfo.timeout;
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="buildInfo">Build Info</param>
        public Build(BuildInfo buildInfo)
        {
            this.buildInfo = buildInfo;
            sProjectDir = sProjectsDir + @"\project-" + buildInfo.project_id;
            commands = new LinkedList<string>();
        }

        /// <summary>
        /// Run the Build Job
        /// </summary>
        public void run()
        {
            state = State.RUNNING;

            // Initialize project dir
            initProjectDir();

            // Add build commands
            foreach (string sCommand in buildInfo.commands)
            {
                commands.AddLast(sCommand);
            }

            // Execute
            foreach (string sCommand in commands)
            {
                if (!exec(sCommand))
                {
                    state = State.FAILED;
                    break;
                }
            }

            if (state == State.RUNNING)
            {
                state = State.SUCCESS;
            }
            __completed = true;
        }

        /// <summary>
        /// Initialize project dir and checkout repo
        /// </summary>
        private void initProjectDir()
        {
            // Check if projects directory exists
            if (!Directory.Exists(sProjectsDir))
            {
                // Create projects directory
                Directory.CreateDirectory(sProjectsDir);
            }

            // Check if already a git repo
            if (Directory.Exists(sProjectDir + @"\.git") && buildInfo.allow_git_fetch)
            {
                // Already a git repo, pull changes
                commands.AddLast(fetchCmd());
                commands.AddLast(checkoutCmd());
            }
            else
            {
                // No git repo, checkout
                if (Directory.Exists(sProjectDir))
                    DeleteDirectory(sProjectDir);

                commands.AddLast(cloneCmd());
                commands.AddLast(checkoutCmd());
            }
        }

        /// <summary>
        /// Execute a command
        /// </summary>
        /// <param name="sCommand">Command to execute</param>
        private bool exec(string sCommand)
        {
            try
            {
                // Remove Whitespaces
                sCommand = sCommand.Trim();

                // Output command
                __output.AddLast("");
                __output.AddLast(sCommand);
                __output.AddLast("");

                // Build process
                Process p = new Process();
                p.StartInfo.UseShellExecute = false;
                if (Directory.Exists(sProjectDir))
                {
                    p.StartInfo.WorkingDirectory = sProjectDir; // Set Current Working Directory to project directory
                }
                p.StartInfo.FileName = "cmd.exe"; // use cmd.exe so we dont have to split our command in file name and arguments
                p.StartInfo.Arguments = "/C \"" + sCommand.Replace("\"", "\\\"") + "\""; // pass full command as arguments

                // Environment variables
                p.StartInfo.EnvironmentVariables["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); // Fix for missing SSH Key

                p.StartInfo.EnvironmentVariables["BUNDLE_GEMFILE"] = sProjectDir + @"\Gemfile";
                p.StartInfo.EnvironmentVariables["BUNDLE_BIN_PATH"] = "";
                p.StartInfo.EnvironmentVariables["RUBYOPT"] = "";

                p.StartInfo.EnvironmentVariables["CI_SERVER"] = "yes";
                p.StartInfo.EnvironmentVariables["CI_SERVER_NAME"] = "GitLab CI";
                p.StartInfo.EnvironmentVariables["CI_SERVER_VERSION"] = null; // GitlabCI Version
                p.StartInfo.EnvironmentVariables["CI_SERVER_REVISION"] = null; // GitlabCI Revision

                p.StartInfo.EnvironmentVariables["CI_BUILD_REF"] = buildInfo.sha;
                p.StartInfo.EnvironmentVariables["CI_BUILD_REF_NAME"] = buildInfo.ref_name;
                p.StartInfo.EnvironmentVariables["CI_BUILD_ID"] = buildInfo.id.ToString();

                // Redirect Standard Output and Standard Error
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.OutputDataReceived += new DataReceivedEventHandler(outputHandler);
                p.ErrorDataReceived += new DataReceivedEventHandler(outputHandler);

                // Run the command
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(iTimeout*1000))
                {
                    p.Kill();
                }
                return p.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// STDOUT/STDERR Handler
        /// </summary>
        /// <param name="sendingProcess">Source process</param>
        /// <param name="outLine">Output Line</param>
        private static void outputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                __output.AddLast(outLine.Data);
            }
        }

        /// <summary>
        /// Get the Checkout CMD
        /// </summary>
        /// <returns>Checkout CMD</returns>
        private string checkoutCmd()
        {
            String sCmd = "";

            // SSH Key Path Fix

            // Change to drive
            sCmd = sProjectDir.Substring(0, 1) + ":";
            // Change to directory
            sCmd += " && cd " + sProjectDir;
            // Git Reset
            sCmd += " && git reset --hard";
            // Git Checkout
            sCmd += " && git checkout " + buildInfo.sha;

            return sCmd;
        }

        /// <summary>
        /// Get the Clone CMD
        /// </summary>
        /// <returns>Clone CMD</returns>
        private string cloneCmd()
        {
            String sCmd = "";

            // Change to drive
            sCmd = sProjectDir.Substring(0, 1) + ":";
            // Change to directory
            sCmd += " && cd " + sProjectsDir;
            // Git Clone
            sCmd += " && git clone " + buildInfo.repo_url + " project-" + buildInfo.project_id;
            // Change to directory
            sCmd += " && cd " + sProjectDir;
            // Git Checkout
            sCmd += " && git checkout " + buildInfo.sha;

            return sCmd;
        }

        /// <summary>
        /// Get the Fetch CMD
        /// </summary>
        /// <returns>Fetch CMD</returns>
        private string fetchCmd()
        {
            String sCmd = "";

            // Change to drive
            sCmd = sProjectDir.Substring(0, 1) + ":";
            // Change to directory
            sCmd += " && cd " + sProjectDir;
            // Git Reset
            sCmd += " && git reset --hard";
            // Git Clean
            sCmd += " && git clean -f";
            // Git fetch
            sCmd += " && git fetch";

            return sCmd;
        }

        /// <summary>
        /// Delete non empty directory tree
        /// </summary>
        private void DeleteDirectory(string target_dir)
        {
            string[] files = Directory.GetFiles(target_dir);
            string[] dirs = Directory.GetDirectories(target_dir);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(target_dir, false);
        }
    }
}
