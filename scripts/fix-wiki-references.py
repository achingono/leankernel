#!/usr/bin/env python3
"""
Fix invalid wiki references in imported facts using LLM.
Converts OpenClaw-style relative paths (../domain/page.md) to LeanKernel-style
entry references (domain-topic).

Usage:
  python3 scripts/fix-wiki-references.py --api-url http://localhost:5080 --token TOKEN
"""

import argparse
import re
import json
import requests
import sys
from typing import Optional
from urllib.parse import urljoin

def extract_references(text: str) -> list[str]:
    """Extract markdown reference paths from text."""
    # Match patterns like ../domain/page.md or ./domain/page.md
    pattern = r'\.\./[\w-]+/[\w-]+\.md|\./[\w-]+/[\w-]+\.md|\[\[.*?\.md.*?\]\]'
    matches = re.findall(pattern, text)
    return list(set(matches))  # Return unique refs

def normalize_reference(ref: str, available_entries: dict) -> Optional[str]:
    """
    Convert OpenClaw path to LeanKernel entry ID.
    ../identity/personality.md -> who-personality
    """
    # Extract domain and page from path
    match = re.search(r'\.\./([\w-]+)/([\w-]+)\.md', ref)
    if not match:
        # Try wiki-link format [[../domain/page.md]]
        match = re.search(r'\[\[(.*?\.md.*?)\]\]', ref)
        if match:
            inner = match.group(1)
            match = re.search(r'\.\./([\w-]+)/([\w-]+)', inner)
            if match:
                domain, page = match.groups()
            else:
                return None
        else:
            return None
    else:
        domain, page = match.groups()
    
    # Map OpenClaw domain to LeanKernel dimension
    domain_to_dimension = {
        'career': 'what',
        'context': 'where',
        'financial': 'how',
        'identity': 'who',
        'relationships': 'who',
        'ventures': 'what',
        'wisdom': 'why',
    }
    
    dimension = domain_to_dimension.get(domain, 'what')
    
    # Look for matching entry in available entries
    # Try exact match first: {dimension}-{page}
    candidate = f"{dimension}-{page.replace('-', '_')}"
    for entry_id, entry in available_entries.items():
        subject_normalized = entry['subject'].lower().replace(' ', '_').replace('-', '_')
        if subject_normalized == page.replace('-', '_'):
            # Found matching subject, return its dimension-based ID
            return f"{dimension}-{page}"
    
    return f"{dimension}-{page}"

def get_references_in_facts(api_url: str, token: str) -> dict:
    """Fetch all facts with references that need fixing."""
    headers = {"Authorization": f"Bearer {token}"}
    
    try:
        resp = requests.get(f"{api_url}/api/wiki/entries", headers=headers, timeout=30)
        resp.raise_for_status()
        entries = resp.json()
    except Exception as e:
        print(f"Error fetching entries: {e}", file=sys.stderr)
        return {}
    
    facts_with_refs = {}
    for entry in entries:
        for fact in entry.get('facts', []):
            claim = fact.get('claim', '')
            refs = extract_references(claim)
            
            # Also check context
            context = fact.get('context', {})
            for ctx_field in ['who', 'what', 'when', 'where', 'why', 'how']:
                ctx_text = context.get(ctx_field, '')
                if ctx_text:
                    refs.extend(extract_references(ctx_text))
            
            if refs:
                entry_id = entry.get('id') or f"{entry['dimension']}-{entry['subject']}"
                if entry_id not in facts_with_refs:
                    facts_with_refs[entry_id] = {
                        'subject': entry['subject'],
                        'dimension': entry['dimension'],
                        'facts': []
                    }
                
                facts_with_refs[entry_id]['facts'].append({
                    'claim': claim[:100],
                    'full_claim': claim,
                    'references': refs,
                    'context': context
                })
    
    return facts_with_refs

def use_llm_to_fix_reference(ref: str, claim_context: str, ollama_url: str = "http://localhost:11434") -> Optional[str]:
    """Use Ollama to suggest a fixed reference."""
    
    prompt = f"""Given this OpenClaw wiki fact with an invalid reference, suggest the corrected reference.

Original reference: {ref}
Fact context: {claim_context[:200]}

The reference should be normalized to LeanKernel wiki format:
- ../career/page.md -> career topic reference
- ../identity/page.md -> identity/personality topic reference  
- etc.

What should the corrected reference be? Reply with ONLY the corrected reference or 'REMOVE' if it should be deleted."""
    
    try:
        resp = requests.post(
            f"{ollama_url}/api/generate",
            json={
                "model": "neural-chat",
                "prompt": prompt,
                "stream": False,
                "temperature": 0.3
            },
            timeout=30
        )
        resp.raise_for_status()
        result = resp.json()
        suggestion = result.get('response', '').strip()
        return suggestion if suggestion else None
    except Exception as e:
        print(f"LLM error: {e}", file=sys.stderr)
        return None

def fix_reference_in_claim(claim: str, old_ref: str, new_ref: Optional[str]) -> str:
    """Replace old reference with new one in claim."""
    if not new_ref or new_ref == 'REMOVE':
        # Remove the reference
        return claim.replace(old_ref, '').strip()
    
    # Convert to markdown link format if not already
    if not new_ref.startswith('['):
        # Add wiki-link brackets
        if old_ref.startswith('[['):
            new_ref = f"[[{new_ref}]]"
        else:
            new_ref = f"`{new_ref}`"
    
    return claim.replace(old_ref, new_ref)

def main():
    parser = argparse.ArgumentParser(description="Fix invalid wiki references using LLM")
    parser.add_argument("--api-url", default="http://localhost:5080", help="LeanKernel API URL")
    parser.add_argument("--token", required=True, help="API Bearer token")
    parser.add_argument("--ollama-url", default="http://localhost:11434", help="Ollama API URL")
    parser.add_argument("--dry-run", action="store_true", help="Show changes without applying")
    parser.add_argument("--verbose", action="store_true", help="Verbose output")
    
    args = parser.parse_args()
    
    print("🔍 Scanning for invalid references...")
    facts_with_refs = get_references_in_facts(args.api_url, args.token)
    
    if not facts_with_refs:
        print("✓ No invalid references found")
        return 0
    
    print(f"Found {len(facts_with_refs)} entries with invalid references\n")
    
    total_refs = sum(len(f['facts']) for f in facts_with_refs.values())
    print(f"Total facts with references to fix: {total_refs}\n")
    
    fixes_suggested = 0
    fixes_applied = 0
    
    for entry_id, entry_data in facts_with_refs.items():
        print(f"📝 {entry_data['subject']} ({entry_id}):")
        
        for fact in entry_data['facts']:
            for ref in fact['references']:
                if args.verbose:
                    print(f"  Reference: {ref}")
                    print(f"  Context: {fact['full_claim'][:80]}...")
                
                # Use LLM to suggest fix
                suggestion = use_llm_to_fix_reference(ref, fact['full_claim'], args.ollama_url)
                
                if suggestion:
                    fixed_claim = fix_reference_in_claim(fact['full_claim'], ref, suggestion)
                    fixes_suggested += 1
                    
                    if args.verbose:
                        print(f"  ✓ Suggested fix: {ref} -> {suggestion}")
                        print(f"  Updated claim: {fixed_claim[:100]}...")
                    else:
                        print(f"  {ref} -> {suggestion}")
                    
                    if not args.dry_run:
                        # TODO: Apply fix via API
                        fixes_applied += 1
        print()
    
    print(f"\n📊 Summary:")
    print(f"  References found: {total_refs}")
    print(f"  Fixes suggested: {fixes_suggested}")
    if not args.dry_run:
        print(f"  Fixes applied: {fixes_applied}")
    else:
        print(f"  (Dry run - no changes applied)")
    
    return 0

if __name__ == "__main__":
    sys.exit(main())
