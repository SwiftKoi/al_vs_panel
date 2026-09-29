/**
 * Helpers for the server-log search box. The box is the single source of truth: facet clicks add or
 * remove `key:value` tokens in it, so what you see is exactly what is searched (and what the URL holds).
 */

export const FILTER_KEYS = [
  "player", "action", "command", "killed", "killedby", "took", "put", "placed", "broke", "gave", "item", "with",
  "level", "log", "source", "near", "sig"
] as const;
export type FilterKey = (typeof FILTER_KEYS)[number];

export interface QueryToken { raw: string; key?: string; value: string; negated: boolean }

/** Splits the box into tokens the same way the server does (quotes keep spaces together). */
export function tokenize(text: string): QueryToken[] {
  const tokens: QueryToken[] = [];
  const pattern = /(-?)(?:([a-z]{1,12}):)?(?:"([^"]*)"?|(\S+))/g;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(text)) !== null) {
    if (!match[0].trim()) continue;
    const [raw, dash, key, quoted, bare] = match;
    const value = quoted ?? bare ?? "";
    tokens.push({ raw, key: key && (FILTER_KEYS as readonly string[]).includes(key) ? key : undefined, value: key && !(FILTER_KEYS as readonly string[]).includes(key) ? `${key}:${value}` : value, negated: !!dash && !!key });
  }
  return tokens;
}

export function formatToken(key: string, value: string, negated = false) {
  const needsQuotes = /[\s"]/.test(value);
  return `${negated ? "-" : ""}${key}:${needsQuotes ? `"${value.replace(/"/g, "")}"` : value}`;
}

const sameFilter = (token: QueryToken, key: string, value: string) =>
  token.key === key && token.value.toLowerCase() === value.toLowerCase();

/** Which state a facet value has in the box: included, excluded, or absent. */
export function filterState(text: string, key: string, value: string): "include" | "exclude" | null {
  const token = tokenize(text).find((t) => sameFilter(t, key, value));
  return token ? (token.negated ? "exclude" : "include") : null;
}

/** Toggles `key:value` (or `-key:value` when excluding); a second click on the same state removes it. */
export function toggleFilter(text: string, key: string, value: string, exclude = false): string {
  const tokens = tokenize(text);
  const existing = tokens.find((t) => sameFilter(t, key, value));
  const rest = tokens.filter((t) => t !== existing).map((t) => t.raw);
  if (!existing || existing.negated !== exclude) rest.push(formatToken(key, value, exclude));
  return rest.join(" ");
}

/** Replaces every token of one key (used for near: and sig:, which take a single value). */
export function setFilter(text: string, key: string, value: string | null): string {
  const rest = tokenize(text).filter((t) => t.key !== key).map((t) => t.raw);
  if (value) rest.push(formatToken(key, value));
  return rest.join(" ");
}

/** Plain search words, for highlighting matches in results. */
export function highlightTerms(terms: string[]): RegExp | null {
  const words = terms
    .flatMap((term) => term.split(/[^\p{L}\p{N}_]+/u))
    .filter((word) => word.length >= 2)
    .map((word) => word.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));
  return words.length ? new RegExp(`(${words.join("|")})`, "giu") : null;
}
