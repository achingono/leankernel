import fs from "node:fs/promises";
import path from "node:path";
import { randomUUID } from "node:crypto";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { chromium } from "playwright";
import { z } from "zod";

const OUTPUT_ROOT = process.env.OUTPUT_ROOT ?? "/app/outputs";
const RUN_TIMEOUT_MS = Number.parseInt(process.env.WEBWRIGHT_RUN_TIMEOUT_MS ?? "120000", 10);

const runs = new Map();

function toIso(date = new Date()) {
  return date.toISOString();
}

function getRun(runId) {
  const run = runs.get(runId);
  if (!run) {
    throw new Error(`Run '${runId}' was not found.`);
  }

  return run;
}

function summarizeRun(run) {
  return {
    runId: run.runId,
    task: run.task,
    status: run.status,
    submittedAt: run.submittedAt,
    startedAt: run.startedAt,
    completedAt: run.completedAt,
    finalDatum: run.finalDatum,
    error: run.error,
    artifacts: run.artifacts.map((artifact) => ({
      id: artifact.id,
      kind: artifact.kind,
      displayName: artifact.displayName,
      bytes: artifact.bytes,
      contentType: artifact.contentType,
    })),
  };
}

async function executeRun(run) {
  run.status = "running";
  run.startedAt = toIso();

  const runDir = path.join(OUTPUT_ROOT, run.runId);
  await fs.mkdir(runDir, { recursive: true });

  let browser;
  try {
    browser = await chromium.launch({ headless: true });
    const context = await browser.newContext();
    const page = await context.newPage();

    if (run.startUrl) {
      await page.goto(run.startUrl, { waitUntil: "domcontentloaded", timeout: RUN_TIMEOUT_MS });
    }

    const screenshotPath = path.join(runDir, "screenshot.png");
    await page.screenshot({ path: screenshotPath, fullPage: false });

    const title = await page.title();
    const url = page.url();
    const stats = await fs.stat(screenshotPath);

    run.finalDatum = `Completed task '${run.task}' on ${url} (title: ${title || "(none)"}).`;
    run.artifacts = [
      {
        id: `screenshot-${run.runId}`,
        kind: "screenshot",
        displayName: "screenshot.png",
        bytes: stats.size,
        contentType: "image/png",
        filePath: screenshotPath,
      },
    ];
    run.status = "succeeded";
  } catch (error) {
    run.status = "failed";
    run.error = error instanceof Error ? error.message : String(error);
  } finally {
    run.completedAt = toIso();
    if (browser) {
      await browser.close();
    }
  }
}

const server = new McpServer({
  name: "leankernel-webwright-mcp",
  version: "1.0.0",
});

server.tool(
  "browser_run_task",
  "Queue a high-level browser task and return a run id.",
  {
    task: z.string().min(1),
    startUrl: z.string().url().optional(),
    requestId: z.string().optional(),
    model: z.string().optional(),
  },
  async ({ task, startUrl }) => {
    const runId = randomUUID();
    const run = {
      runId,
      task,
      startUrl,
      status: "queued",
      submittedAt: toIso(),
      startedAt: null,
      completedAt: null,
      finalDatum: null,
      error: null,
      artifacts: [],
    };

    runs.set(runId, run);
    void executeRun(run);

    return {
      content: [
        {
          type: "text",
          text: JSON.stringify({ runId, status: run.status, submittedAt: run.submittedAt }),
        },
      ],
    };
  }
);

server.tool(
  "browser_get_run",
  "Get current status for a previously queued browser run.",
  {
    runId: z.string().min(1),
  },
  async ({ runId }) => {
    const run = getRun(runId);
    return {
      content: [{ type: "text", text: JSON.stringify(summarizeRun(run)) }],
    };
  }
);

server.tool(
  "browser_cancel_run",
  "Cancel a queued or running browser run.",
  {
    runId: z.string().min(1),
  },
  async ({ runId }) => {
    const run = getRun(runId);
    if (run.status === "queued" || run.status === "running") {
      run.status = "cancelled";
      run.completedAt = toIso();
      run.error = "Cancelled by caller.";
    }

    return {
      content: [
        {
          type: "text",
          text: JSON.stringify({ runId: run.runId, status: run.status }),
        },
      ],
    };
  }
);

server.tool(
  "browser_get_artifact",
  "Read a run artifact and return metadata plus base64 payload.",
  {
    runId: z.string().min(1),
    artifactId: z.string().min(1),
  },
  async ({ runId, artifactId }) => {
    const run = getRun(runId);
    const artifact = run.artifacts.find((candidate) => candidate.id === artifactId);
    if (!artifact) {
      throw new Error(`Artifact '${artifactId}' was not found for run '${runId}'.`);
    }

    const payload = await fs.readFile(artifact.filePath);
    return {
      content: [
        {
          type: "text",
          text: JSON.stringify({
            id: artifact.id,
            kind: artifact.kind,
            displayName: artifact.displayName,
            contentType: artifact.contentType,
            bytes: artifact.bytes,
            dataBase64: payload.toString("base64"),
          }),
        },
      ],
    };
  }
);

await fs.mkdir(OUTPUT_ROOT, { recursive: true });
const transport = new StdioServerTransport();
await server.connect(transport);
