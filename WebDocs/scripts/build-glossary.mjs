// Generates docs/reference/glossary.md from the spec corpus at ../specs/glossary/blocks.
// Run with: npm run glossary   (from WebDocs)
import { readdirSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const blocksDir = resolve(here, '../../specs/glossary/blocks')
const outFile = resolve(here, '../docs/reference/glossary.md')

const terms = readdirSync(blocksDir)
  .filter((f) => f.endsWith('.json'))
  .map((f) => JSON.parse(readFileSync(join(blocksDir, f), 'utf8').replace(/^﻿/, '')))
  .filter((b) => b.type === 'Term' && b.fields?.name && b.fields?.definition)
  .filter((b) => (b.metadata?.status ?? 'Draft') !== 'Retired')
  .sort((a, b) => a.fields.name.localeCompare(b.fields.name, 'en'))

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
const slug = (s) =>
  s
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '')

const byLetter = new Map()
for (const t of terms) {
  const letter = t.fields.name[0].toUpperCase()
  if (!byLetter.has(letter)) byLetter.set(letter, [])
  byLetter.get(letter).push(t)
}

let md = `---
title: Glossary
description: Definitions of every term ArgentSea uses, from shard set and shard key to fan-out, resilience strategy and grain persistence.
outline: false
---

# Glossary

ArgentSea has a small, precise vocabulary. This page is generated from the project's specification corpus, so it is the authoritative meaning of each term as the current code uses it.

<GlossaryIndex :letters='${JSON.stringify([...byLetter.keys()])}' />

`

for (const [letter, list] of byLetter) {
  md += `## ${letter} {#letter-${letter.toLowerCase()}}\n\n`
  for (const t of list) {
    const syn = (t.fields.synonyms ?? []).filter((s) => s && s !== t.fields.name)
    md += `### ${t.fields.name} {#${slug(t.fields.name)}}\n\n`
    if (syn.length) md += `<p class="glossary-synonyms">Also: ${syn.map((s) => `<code>${esc(s)}</code>`).join(', ')}</p>\n\n`
    md += `${t.fields.definition.trim()}\n\n`
  }
}

mkdirSync(dirname(outFile), { recursive: true })
writeFileSync(outFile, md, 'utf8')
console.log(`Wrote ${terms.length} terms to ${outFile}`)
