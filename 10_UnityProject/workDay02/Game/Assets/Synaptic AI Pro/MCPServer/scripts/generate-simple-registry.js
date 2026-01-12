#!/usr/bin/env node

/**
 * generate-simple-registry.js
 *
 * Generates tool-registry.json WITHOUT embeddings (no API key required)
 * This allows basic category filtering but not semantic search
 *
 * Usage:
 *   node scripts/generate-simple-registry.js
 */

import { detectCategory } from '../utils/tool-loader.js';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Load existing index.js to extract tool definitions
function extractToolsFromIndexJs() {
    const indexPath = path.join(__dirname, '..', 'index.js');
    const content = fs.readFileSync(indexPath, 'utf-8');

    const tools = [];

    // Pattern 1: Single-quoted description
    const singleQuoteRegex = /mcpServer\.registerTool\('([^']+)',\s*\{[^}]*?title:\s*'([^']+)',\s*description:\s*'([^']+)'/g;

    // Pattern 2: Template literal (backtick) description - extract first line
    const backtickRegex = /mcpServer\.registerTool\('([^']+)',\s*\{[^}]*?title:\s*'([^']+)',\s*description:\s*`([^`]+)`/g;

    let match;

    // Find single-quoted descriptions
    while ((match = singleQuoteRegex.exec(content)) !== null) {
        const [, name, title, description] = match;
        tools.push({
            name,
            title,
            description: description.substring(0, 500) // Limit length
        });
    }

    // Find backtick descriptions
    while ((match = backtickRegex.exec(content)) !== null) {
        const [, name, title, description] = match;
        // Get first line or first 500 chars for description
        const firstLine = description.split('\n')[0].trim();
        tools.push({
            name,
            title,
            description: firstLine.substring(0, 500)
        });
    }

    // Remove duplicates (in case both patterns match)
    const uniqueTools = [];
    const seenNames = new Set();
    for (const tool of tools) {
        if (!seenNames.has(tool.name)) {
            seenNames.add(tool.name);
            uniqueTools.push(tool);
        }
    }

    console.log(`[Generator] Found ${uniqueTools.length} tools in index.js`);
    return uniqueTools;
}

function generateRegistry() {
    console.log('[Generator] Starting simple tool registry generation (no embeddings)...');

    const tools = extractToolsFromIndexJs();

    if (tools.length === 0) {
        console.error('[Generator] ERROR: No tools found in index.js');
        process.exit(1);
    }

    const registry = {};

    for (const tool of tools) {
        // Detect category
        const category = detectCategory(tool.name);

        registry[tool.name] = {
            title: tool.title,
            description: tool.description,
            category: category,
            embedding: null  // No embedding for simple version
        };
    }

    // Write to file
    const outputPath = path.join(__dirname, '..', 'tool-registry.json');
    fs.writeFileSync(outputPath, JSON.stringify(registry, null, 2));

    console.log(`[Generator] ✅ Successfully generated tool-registry.json (without embeddings)`);
    console.log(`[Generator] Location: ${outputPath}`);
    console.log(`[Generator] Total tools: ${Object.keys(registry).length}`);

    // Category breakdown
    const categoryCount = {};
    for (const meta of Object.values(registry)) {
        categoryCount[meta.category] = (categoryCount[meta.category] || 0) + 1;
    }

    console.log('[Generator] Category breakdown:');
    for (const [category, count] of Object.entries(categoryCount).sort((a, b) => b[1] - a[1])) {
        console.log(`  ${category}: ${count} tools`);
    }

    console.log('[Generator] Note: Semantic search disabled (no embeddings). Category filtering available.');
}

generateRegistry();
