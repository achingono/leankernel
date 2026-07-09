#!/usr/bin/env node

const fs = require('fs');
const path = require('path');
const { glob } = require('glob');
const { parseStringPromise } = require('xml2js');

// Coverage thresholds
const COVERAGE_THRESHOLD_HIGH = 0.8;
const COVERAGE_THRESHOLD_LOW = 0.6;
const COVERAGE_THRESHOLD_VERY_LOW = 0.4;
const MAX_COVERAGE_ANNOTATIONS = 50;

// GitHub Actions annotations
function buildLocation(file, line) {
  if (!file) return '';
  let loc = `file=${file}`;
  if (line) loc += `,line=${line}`;
  return loc;
}

function error(message, file, line) {
  console.log(`::error ${buildLocation(file, line)}::${message}`);
}

function warning(message, file, line) {
  console.log(`::warning ${buildLocation(file, line)}::${message}`);
}

function notice(message) {
  console.log(`::notice ::${message}`);
}

function createEmptyCoverage() {
  return {
    lineRate: 0,
    branchRate: 0,
    linesCovered: 0,
    linesValid: 0,
    branchesCovered: 0,
    branchesValid: 0,
    files: []
  };
}

function parseCoberturaClass(cls) {
  const fileName = cls.$.filename;
  if (!fileName || typeof fileName !== 'string') return null;
  const classLineRate = parseFloat(cls.$['line-rate'] || 0) || 0;
  const classBranchRate = parseFloat(cls.$['branch-rate'] || 0) || 0;

  const lines = cls.lines?.[0]?.line || [];
  const uncoveredLines = [];
  for (const line of lines) {
    if (parseInt(line.$.hits || 0) === 0) {
      uncoveredLines.push(parseInt(line.$.number));
    }
  }

  return {
    name: fileName,
    lineRate: classLineRate,
    branchRate: classBranchRate,
    uncoveredLines: uncoveredLines.sort((a, b) => a - b)
  };
}

// Parse Cobertura coverage file
async function parseCoberturaFile(filePath) {
  try {
    const content = fs.readFileSync(filePath, 'utf8');
    const result = await parseStringPromise(content);
    const coverage = createEmptyCoverage();

    if (!result.coverage) {
      console.log(`Warning: Invalid Cobertura XML format in ${filePath}`);
      return coverage;
    }

    const attrs = result.coverage.$;
    coverage.lineRate = parseFloat(attrs['line-rate'] || 0) || 0;
    coverage.branchRate = parseFloat(attrs['branch-rate'] || 0) || 0;
    coverage.linesCovered = parseInt(attrs['lines-covered'] || '0', 10) || 0;
    coverage.linesValid = parseInt(attrs['lines-valid'] || '0', 10) || 0;
    coverage.branchesCovered = parseInt(attrs['branches-covered'] || '0', 10) || 0;
    coverage.branchesValid = parseInt(attrs['branches-valid'] || '0', 10) || 0;

    const packages = result.coverage.packages?.[0]?.package || [];
    for (const pkg of packages) {
      const classes = pkg.classes?.[0]?.class || [];
      for (const cls of classes) {
        const fileEntry = parseCoberturaClass(cls);
        if (fileEntry) {
          coverage.files.push(fileEntry);
        }
      }
    }

    return coverage;
  } catch (err) {
    console.log(`Warning: Failed to parse coverage file ${filePath}: ${err.message}`);
    return createEmptyCoverage();
  }
}

// Parse JUnit XML file (Playwright reporter output)
async function parseJUnitFile(filePath) {
  const content = fs.readFileSync(filePath, 'utf8');
  const result = await parseStringPromise(content);

  const testResults = {
    total: 0,
    passed: 0,
    failed: 0,
    skipped: 0,
    failures: []
  };

  const testsuites = result.testsuites?.testsuite || [];
  for (const suite of testsuites) {
    const tests = parseInt(suite.$.tests || 0);
    const failures = parseInt(suite.$.failures || 0);
    const skipped = parseInt(suite.$.skipped || 0);
    const errors = parseInt(suite.$.errors || 0);

    testResults.total += tests;
    testResults.failed += failures + errors;
    testResults.skipped += skipped;
    testResults.passed += tests - failures - errors - skipped;

    const testcases = suite.testcase || [];
    for (const tc of testcases) {
      const hasFailure = tc.failure || tc.error;
      if (hasFailure) {
        const failNode = (tc.failure || tc.error)[0];
        const message = typeof failNode === 'string' ? failNode : (failNode.$?.message || failNode._ || 'Test failed');

        // Try to extract file and line from the failure message/body
        let file = null;
        let line = null;
        const body = typeof failNode === 'string' ? failNode : (failNode._ || '');
        const match = body.match(/at (?:.*?)[(]?([\w./\\-]+\.[jt]sx?):(\d+):\d+/);
        if (match) {
          file = match[1];
          line = parseInt(match[2]);
        }

        testResults.failures.push({
          name: tc.$.name || 'Unknown Test',
          className: tc.$.classname || suite.$.name || '',
          message: typeof message === 'string' ? message.substring(0, 500) : String(message).substring(0, 500),
          stackTrace: body.substring(0, 2000),
          file,
          line
        });
      }
    }
  }

  return testResults;
}

function parseTrxFailure(testResult, testDefinitions) {
  const testId = testResult.$.testId;
  const testDef = testDefinitions.get(testId) || {};
  const testName = testResult.$.testName || testDef.name || 'Unknown Test';
  const className = testDef.className || '';
  const output = testResult.Output?.[0];
  const errorInfo = output?.ErrorInfo?.[0];
  const message = errorInfo?.Message?.[0] || 'Test failed';
  const stackTrace = errorInfo?.StackTrace?.[0] || '';

  let file = null;
  let line = null;
  const stackMatch = stackTrace.match(/in (.+\.cs):line (\d+)/);
  if (stackMatch) {
    file = stackMatch[1];
    line = parseInt(stackMatch[2]);
  }

  return { name: testName, className, message, stackTrace, file, line };
}

// Parse TRX file (.NET test results)
async function parseTrxFile(filePath) {
  const content = fs.readFileSync(filePath, 'utf8');
  const result = await parseStringPromise(content);

  const testResults = {
    total: 0,
    passed: 0,
    failed: 0,
    skipped: 0,
    failures: []
  };

  if (!result.TestRun) {
    console.log('Invalid TRX file format');
    return testResults;
  }

  const resultSummary = result.TestRun.ResultSummary?.[0];
  if (resultSummary) {
    const counters = resultSummary.Counters?.[0].$;
    if (counters) {
      testResults.total = parseInt(counters.total || 0);
      testResults.passed = parseInt(counters.passed || 0);
      testResults.failed = parseInt(counters.failed || 0);
      testResults.skipped = parseInt(counters.notExecuted || 0) + parseInt(counters.inconclusive || 0);
    }
  }

  const testDefinitions = new Map();
  if (result.TestRun.TestDefinitions?.[0]?.UnitTest) {
    for (const unitTest of result.TestRun.TestDefinitions[0].UnitTest) {
      const id = unitTest.$.id;
      const name = unitTest.$.name;
      const className = unitTest.TestMethod?.[0]?.$.className;
      testDefinitions.set(id, { name, className });
    }
  }

  if (result.TestRun.Results?.[0]?.UnitTestResult) {
    for (const testResult of result.TestRun.Results[0].UnitTestResult) {
      if (testResult.$.outcome === 'Failed') {
        testResults.failures.push(parseTrxFailure(testResult, testDefinitions));
      }
    }
  }

  return testResults;
}

// Auto-detect format and parse
async function parseResultFile(filePath) {
  const content = fs.readFileSync(filePath, 'utf8');
  if (content.includes('<testsuites') || content.includes('<testsuite')) {
    console.log(`  Format: JUnit XML`);
    return parseJUnitFile(filePath);
  } else if (content.includes('<TestRun') || content.includes('.trx')) {
    console.log(`  Format: TRX`);
    return parseTrxFile(filePath);
  }
  console.log(`  Warning: Unknown format, attempting JUnit parse`);
  return parseJUnitFile(filePath);
}

function coverageIcon(rate) {
  if (rate >= COVERAGE_THRESHOLD_HIGH) return '✅';
  if (rate >= COVERAGE_THRESHOLD_LOW) return '⚠️';
  return '❌';
}

function buildTestResultsTable(results, passRate) {
  let table = `### 📊 Test Results\n\n`;
  table += `| Status | Count |\n`;
  table += `|--------|-------|\n`;
  table += `| Total | ${results.total} |\n`;
  table += `| Passed | ${results.passed} |\n`;
  table += `| Failed | ${results.failed} |\n`;
  table += `| Skipped | ${results.skipped} |\n`;
  table += `| Pass Rate | ${passRate}% |\n`;
  return table;
}

function buildCoverageSection(coverage) {
  if (!coverage || coverage.linesValid <= 0) return '';
  const lineCoveragePercent = (coverage.lineRate * 100).toFixed(1);
  const branchCoveragePercent = (coverage.branchRate * 100).toFixed(1);
  const icon = coverageIcon(coverage.lineRate);

  let section = `\n### ${icon} Code Coverage\n\n`;
  section += `| Metric | Coverage |\n`;
  section += `|--------|----------|\n`;
  section += `| Line Coverage | ${lineCoveragePercent}% (${coverage.linesCovered}/${coverage.linesValid}) |\n`;
  section += `| Branch Coverage | ${branchCoveragePercent}% (${coverage.branchesCovered}/${coverage.branchesValid}) |\n`;

  const lowCoverageFiles = coverage.files
    .filter(f => f.lineRate < COVERAGE_THRESHOLD_LOW)
    .sort((a, b) => a.lineRate - b.lineRate);
  if (lowCoverageFiles.length > 0) {
    section += `\n#### ⚠️ Files with Low Coverage (< ${(COVERAGE_THRESHOLD_LOW * 100).toFixed(0)}%)\n\n`;
    section += `| File | Line Coverage |\n`;
    section += `|------|---------------|\n`;
    for (const file of lowCoverageFiles.slice(0, 10)) {
      const fileName = file.name.split('/').pop();
      const coveragePercent = (file.lineRate * 100).toFixed(1);
      section += `| \`${fileName}\` | ${coveragePercent}% |\n`;
    }
    if (lowCoverageFiles.length > 10) {
      section += `\n_... and ${lowCoverageFiles.length - 10} more files_\n`;
    }
  }
  return section;
}

function buildFailuresSection(failures) {
  if (failures.length === 0) return '';
  let section = `\n### ❌ Failed Tests\n\n`;
  for (const failure of failures) {
    section += `#### ${failure.name}\n`;
    if (failure.className) {
      section += `**Class:** \`${failure.className}\`\n\n`;
    }
    section += `**Error:**\n\`\`\`\n${failure.message}\n\`\`\`\n\n`;
    if (failure.stackTrace) {
      let stackTrace = failure.stackTrace;
      if (stackTrace.length > 1000) {
        stackTrace = stackTrace.substring(0, 1000) + '\n... (truncated)';
      }
      section += `<details>\n<summary>Stack Trace</summary>\n\n\`\`\`\n${stackTrace}\n\`\`\`\n</details>\n\n`;
    }
    section += '---\n\n';
  }
  return section;
}

// Create summary comment
function createSummary(results, coverage, reportName) {
  const passRate = results.total > 0
    ? ((results.passed / results.total) * 100).toFixed(1)
    : 0;
  const icon = results.failed === 0 ? '✅' : '❌';

  let summary = `## ${icon} ${reportName}\n\n`;
  summary += buildTestResultsTable(results, passRate);
  summary += buildCoverageSection(coverage);
  summary += buildFailuresSection(results.failures);
  return summary;
}

async function findExistingBotComment(fetch, commentsUrl, token) {
  const commentsResponse = await fetch(commentsUrl, {
    headers: {
      'Authorization': `token ${token}`,
      'Accept': 'application/vnd.github.v3+json'
    }
  });

  if (!commentsResponse.ok) return null;
  const comments = await commentsResponse.json();
  return comments.find(c => c.user.type === 'Bot' && c.body.includes('Test Results')) || null;
}

async function updateComment(fetch, updateUrl, summary, token) {
  const response = await fetch(updateUrl, {
    method: 'PATCH',
    headers: {
      'Authorization': `token ${token}`,
      'Accept': 'application/vnd.github.v3+json',
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({ body: summary })
  });

  if (response.ok) {
    console.log('Updated existing PR comment');
    return true;
  }
  return false;
}

async function createComment(fetch, commentsUrl, summary, token) {
  const response = await fetch(commentsUrl, {
    method: 'POST',
    headers: {
      'Authorization': `token ${token}`,
      'Accept': 'application/vnd.github.v3+json',
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({ body: summary })
  });

  if (response.ok) {
    console.log('Posted test results to PR');
  } else {
    console.log(`Failed to post PR comment: ${response.statusText}`);
  }
}

// Post comment to PR
async function postPrComment(summary) {
  const token = process.env.GITHUB_TOKEN;
  const repository = process.env.GITHUB_REPOSITORY;
  const prNumber = process.env.GITHUB_REF?.match(/refs\/pull\/(\d+)\//)?.[1];

  if (!token || !repository || !prNumber) {
    console.log('Not a PR event or missing required environment variables. Skipping PR comment.');
    return;
  }

  const [owner, repo] = repository.split('/');
  const commentsUrl = `https://api.github.com/repos/${owner}/${repo}/issues/${prNumber}/comments`;

  try {
    const fetch = (await import('node-fetch')).default;

    const botComment = await findExistingBotComment(fetch, commentsUrl, token);
    if (botComment) {
      const updateUrl = `https://api.github.com/repos/${owner}/${repo}/issues/comments/${botComment.id}`;
      if (await updateComment(fetch, updateUrl, summary, token)) return;
    }

    await createComment(fetch, commentsUrl, summary, token);
  } catch (err) {
    console.log(`Error posting PR comment: ${err.message}`);
  }
}

async function aggregateCoverage(coveragePath) {
  console.log(`Looking for coverage files: ${coveragePath}`);
  const coverageFiles = await glob(coveragePath, {
    windowsPathsNoEscape: true,
    absolute: false
  });

  if (coverageFiles.length === 0) {
    console.log('No coverage files found');
    return null;
  }

  console.log(`Found ${coverageFiles.length} coverage file(s)`);
  const allCoverage = createEmptyCoverage();

  for (const file of coverageFiles) {
    console.log(`Parsing coverage: ${file}`);
    try {
      const coverage = await parseCoberturaFile(file);
      allCoverage.linesCovered += coverage.linesCovered;
      allCoverage.linesValid += coverage.linesValid;
      allCoverage.branchesCovered += coverage.branchesCovered;
      allCoverage.branchesValid += coverage.branchesValid;

      for (const newFile of coverage.files) {
        const existingFile = allCoverage.files.find(f => f.name === newFile.name);
        if (existingFile) {
          const uncoveredSet = new Set([...existingFile.uncoveredLines, ...newFile.uncoveredLines]);
          existingFile.uncoveredLines = Array.from(uncoveredSet).sort((a, b) => a - b);
          existingFile.lineRate = Math.min(existingFile.lineRate, newFile.lineRate);
          existingFile.branchRate = Math.min(existingFile.branchRate, newFile.branchRate);
        } else {
          allCoverage.files.push(newFile);
        }
      }
    } catch (err) {
      console.log(`Warning: Failed to parse coverage file ${file}: ${err.message}`);
    }
  }

  if (allCoverage.linesValid > 0) {
    allCoverage.lineRate = allCoverage.linesCovered / allCoverage.linesValid;
  }
  if (allCoverage.branchesValid > 0) {
    allCoverage.branchRate = allCoverage.branchesCovered / allCoverage.branchesValid;
  }

  return allCoverage;
}

function emitCoverageAnnotations(allCoverage) {
  if (!allCoverage) return;
  let annotationCount = 0;
  const sortedFiles = allCoverage.files.slice().sort((a, b) => a.lineRate - b.lineRate);
  for (const file of sortedFiles) {
    if (annotationCount >= MAX_COVERAGE_ANNOTATIONS) break;
    if (file.lineRate < COVERAGE_THRESHOLD_VERY_LOW && file.uncoveredLines.length > 0) {
      const lineRange = file.uncoveredLines.length > 5
        ? `${file.uncoveredLines.slice(0, 5).join(', ')}, ...`
        : file.uncoveredLines.join(', ');
      warning(
        `Low coverage: ${(file.lineRate * 100).toFixed(1)}% - ${file.uncoveredLines.length} uncovered lines (${lineRange})`,
        file.name,
        file.uncoveredLines[0]
      );
      annotationCount++;
    }
  }
}

function emitFailureAnnotations(failures) {
  for (const failure of failures) {
    const message = `${failure.name}: ${failure.message}`;
    if (failure.file && failure.line) {
      error(message, failure.file, failure.line);
    } else {
      error(message);
    }
  }
}

// Main execution
async function main() {
  try {
    const testResultsPath = process.env.TEST_RESULTS_PATH;
    const coveragePath = process.env.COVERAGE_PATH;
    const reportName = process.env.REPORT_NAME || 'Test Results';
    const failOnError = process.env.FAIL_ON_ERROR === 'true';

    console.log(`Looking for test results: ${testResultsPath}`);

    const files = await glob(testResultsPath, {
      windowsPathsNoEscape: true,
      absolute: false
    });

    if (files.length === 0) {
      warning(`No test result files found matching: ${testResultsPath}`);
      return;
    }

    console.log(`Found ${files.length} test result file(s)`);

    const allResults = { total: 0, passed: 0, failed: 0, skipped: 0, failures: [] };

    for (const file of files) {
      console.log(`Parsing: ${file}`);
      const results = await parseResultFile(file);
      allResults.total += results.total;
      allResults.passed += results.passed;
      allResults.failed += results.failed;
      allResults.skipped += results.skipped;
      allResults.failures.push(...results.failures);
    }

    const allCoverage = coveragePath ? await aggregateCoverage(coveragePath) : null;
    emitCoverageAnnotations(allCoverage);
    emitFailureAnnotations(allResults.failures);

    const summary = createSummary(allResults, allCoverage, reportName);
    console.log('\n' + summary);

    await postPrComment(summary);

    if (process.env.GITHUB_STEP_SUMMARY) {
      fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary);
    }

    if (allResults.failed === 0) {
      notice(`✅ All ${allResults.passed} tests passed!`);
    } else {
      notice(`❌ ${allResults.failed} out of ${allResults.total} tests failed`);
    }

    if (allCoverage && allCoverage.linesValid > 0) {
      const coveragePercent = (allCoverage.lineRate * 100).toFixed(1);
      notice(`📊 Code coverage: ${coveragePercent}% (${allCoverage.linesCovered}/${allCoverage.linesValid} lines)`);
    }

    if (failOnError && allResults.failed > 0) {
      process.exit(1);
    }

  } catch (err) {
    error(`Failed to process test results: ${err.message}`);
    console.error(err.stack);
    process.exit(1);
  }
}

main();
