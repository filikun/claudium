"use strict";
/**
 * Copyright (c) 2019, Microsoft Corporation (MIT License).
 *
 * This module fetches the console process list for a particular PID. It must be
 * called from a different process (child_process.fork) as there can only be a
 * single console attached to a process.
 */
Object.defineProperty(exports, "__esModule", { value: true });
var utils_1 = require("./utils");
var getConsoleProcessList = utils_1.loadNativeModule('conpty_console_list').module.getConsoleProcessList;
var shellPid = parseInt(process.argv[2], 10);
var consoleProcessList = getConsoleProcessList(shellPid);
process.send({ consoleProcessList: consoleProcessList });
process.exit(0);//# sourceMappingURL=https://main.vscode-cdn.net/sourcemaps/61b3d0ab13be7dda2389f1d3e60a119c7f660cc3/node_modules/node-pty/lib/conpty_console_list_agent.js.map