import { memo, useMemo, useState } from "react";
import {
  JamlBoss,
  JamlGameCard,
  JamlTag,
  JamlVoucher,
  JimboBadge,
  JimboButton,
  JimboPanel,
  JimboSeedCopyChip,
  JimboText,
} from "jaml-ui";
import { decodeMotelyItemToJamlCard } from "jaml-ui/motely";
import { BOOSTER_PACKS, BOSSES, TAGS, VOUCHERS } from "jaml-ui/sprites";
import { JimboBox, JimboRow, JimboShopBelt, JimboSpinner, JimboStack } from "jaml-ui/ui";
import {
  MotelyBoosterPack,
  MotelyBossBlind,
  MotelyDeck,
  MotelyItemEdition,
  MotelyItemType,
  MotelyJoker,
  MotelyStake,
  MotelyTag,
  MotelyVoucher,
  type MotelyItem,
  type MotelyJamlyzerPack,
  type MotelyJamlyzerSeedResult,
} from "motely-wasm";

function camelToSpaced(value: string): string {
  return value.replace(/([A-Z])/g, " $1").trim();
}

function normKey(value: string): string {
  return value.replace(/[^a-zA-Z0-9]/g, "").toLowerCase();
}

function matchSpriteName(
  enumName: string | undefined,
  catalog: readonly { name: string }[],
  normalize: (raw: string) => string,
  fallback: (raw: string) => string,
): string {
  if (!enumName) return "";
  const target = normKey(normalize(enumName));
  const hit = catalog.find((entry) => normKey(entry.name) === target);
  return hit ? hit.name : fallback(enumName);
}

function bossLabel(boss: MotelyBossBlind): string {
  return matchSpriteName(MotelyBossBlind[boss], BOSSES, (n) => n, camelToSpaced) || "Small Blind";
}

function voucherLabel(voucher: MotelyVoucher): string {
  return matchSpriteName(MotelyVoucher[voucher], VOUCHERS, (n) => n, camelToSpaced);
}

function tagLabel(tag: MotelyTag): string {
  return matchSpriteName(MotelyTag[tag], TAGS, (n) => n, camelToSpaced);
}

function packLabel(pack: MotelyBoosterPack): string {
  return matchSpriteName(
    MotelyBoosterPack[pack],
    BOOSTER_PACKS,
    (n) => `${n}pack`,
    (n) => `${camelToSpaced(n)} Pack`,
  );
}

function deckLabel(deck: MotelyDeck): string {
  return camelToSpaced(MotelyDeck[deck] ?? "");
}

function stakeLabel(stake: MotelyStake): string {
  return camelToSpaced(MotelyStake[stake] ?? "");
}

function legendaryLabel(joker: MotelyJoker, edition: MotelyItemEdition): string {
  const name = camelToSpaced(MotelyJoker[joker] ?? String(joker));
  return edition !== MotelyItemEdition.None ? `${MotelyItemEdition[edition]} ${name}` : name;
}

const ShopCard = memo(function ShopCard({
  item,
  slotNumber,
  watched,
  scale = 0.85,
}: {
  item: MotelyItem;
  slotNumber?: number;
  watched?: boolean;
  scale?: number;
}) {
  const decoded = useMemo(() => decodeMotelyItemToJamlCard(item, scale), [item, scale]);
  if (!decoded) {
    return (
      <JimboBadge size="md" tone="grey" title="Unrecognized item">
        ?
      </JimboBadge>
    );
  }
  return (
    <JimboStack
      gap="xs"
      align="center"
      style={
        watched
          ? {
              padding: "0.2rem",
              borderRadius: 8,
              outline: "2px solid var(--j-green)",
              background: "rgba(75, 194, 146, 0.12)",
            }
          : undefined
      }
    >
      <JamlGameCard type={decoded.type} card={decoded.card} hoverTilt />
      <JimboText size="micro" tone={watched ? "green" : "white"} className="j-text-center">
        {slotNumber !== undefined ? `#${slotNumber} ` : ""}
        {decoded.card.name}
      </JimboText>
    </JimboStack>
  );
});

const PackRow = memo(function PackRow({
  pack,
  watchedKeys,
}: {
  pack: MotelyJamlyzerPack;
  watchedKeys: ReadonlySet<string>;
}) {
  return (
    <JimboBox className="j-jamlyzer-pack-line">
      <JimboText size="xs" tone="grey">
        {packLabel(pack.pack)}:
      </JimboText>
      <JimboBox className="j-shop-belt hide-scrollbar">
        {pack.items.map((item, idx) => (
          <ShopCard
            key={idx}
            item={item}
            watched={watchedKeys.has(normKey(MotelyItemType[item.type] ?? ""))}
          />
        ))}
      </JimboBox>
    </JimboBox>
  );
});

export interface InfiniteJamlyzerViewProps {
  result: MotelyJamlyzerSeedResult;
  deck?: MotelyDeck;
  stake?: MotelyStake;
  /** Normalized watch names (lowercase alphanumeric) to highlight matching shop & pack cards. */
  watchedKeys?: ReadonlySet<string>;
  /** Called when the user scrolls near the end of the shop belt or clicks +50 / +200. */
  onLoadMoreShop?: (count?: number) => void;
}

/**
 * Full Balatro seed inspector with true infinite shop scrolling backed by
 * `MotelyJamlyzer.resume(config, streamStates, 0, pageSize)`.
 *
 * Replaces jaml-ui's fixed-depth JamlyzerView so scrolling or dragging to the right edge of
 * any ante's shop belt rolls the next window in O(pageSize) without re-rolling earlier slots.
 */
export function InfiniteJamlyzerView({
  result,
  deck,
  stake,
  watchedKeys,
  onLoadMoreShop,
}: InfiniteJamlyzerViewProps) {
  const minAnte = result.antes[0]?.ante ?? 1;
  const maxAnte = result.antes[result.antes.length - 1]?.ante ?? minAnte;

  const [selectedAnte, setSelectedAnte] = useState<number>(() => {
    const ante1 = result.antes.find((a) => a.ante === 1);
    return ante1 ? 1 : minAnte;
  });

  const activeAnte = useMemo(() => {
    const exact = result.antes.find((a) => a.ante === selectedAnte);
    return exact ?? result.antes[0];
  }, [result.antes, selectedAnte]);

  const emptyWatch = useMemo(() => new Set<string>(), []);
  const watchSet = watchedKeys ?? emptyWatch;

  if (!activeAnte) {
    return (
      <JimboPanel body>
        <JimboText tone="grey">No ante data for seed {result.seed}.</JimboText>
      </JimboPanel>
    );
  }

  const currentAnteNum = activeAnte.ante;
  const stepAnte = (delta: number) =>
    setSelectedAnte((curr) => Math.max(minAnte, Math.min(maxAnte, curr + delta)));

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "ArrowUp") {
      event.preventDefault();
      stepAnte(-1);
    } else if (event.key === "ArrowDown") {
      event.preventDefault();
      stepAnte(1);
    }
  };

  const smallTag = tagLabel(activeAnte.smallBlindTag);
  const bigTag = tagLabel(activeAnte.bigBlindTag);
  const boss = bossLabel(activeAnte.boss);
  const voucher = voucherLabel(activeAnte.voucher);

  return (
    <JimboPanel body={false} className="j-jamlyzer-view" tabIndex={0} onKeyDown={onKeyDown}>
      <JimboStack gap="md" align="stretch">
        <JimboSpinner
          value={`Ante ${currentAnteNum}`}
          onPrev={() => stepAnte(-1)}
          onNext={() => stepAnte(1)}
          canPrev={currentAnteNum > minAnte}
          canNext={currentAnteNum < maxAnte}
        />

        <JimboRow gap="md" align="center" style={{ flexWrap: "wrap" }}>
          <JimboSeedCopyChip value={result.seed} />
          <JimboText size="xs" tone="gold">
            {result.score}
          </JimboText>
          {deck !== undefined ? (
            <JimboText size="xs" tone="grey">
              {deckLabel(deck)}
            </JimboText>
          ) : null}
          {stake !== undefined ? (
            <JimboText size="xs" tone="grey">
              {stakeLabel(stake)}
            </JimboText>
          ) : null}
          <JimboBadge size="sm" tone="blue">
            {activeAnte.shopItems.length.toLocaleString()} shop cards loaded
          </JimboBadge>
        </JimboRow>

        {result.legendaryOrder.length > 0 ? (
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: "0.35rem" }}>
            <JimboText size="xs" tone="gold">
              Soul order:
            </JimboText>
            {result.legendaryOrder.map((leg, i) => (
              <JimboBadge key={i} size="sm" tone="orange">
                A{leg.ante} P{leg.packSlot + 1}: {legendaryLabel(leg.joker, leg.edition)}
              </JimboBadge>
            ))}
          </div>
        ) : null}

        <JimboBox className="j-jamlyzer-ante-head">
          <JimboStack gap="xs" align="center">
            <JimboText size="micro" tone="grey">
              Voucher
            </JimboText>
            <JamlVoucher voucherName={voucher} scale={0.7} />
            <JimboText size="micro" className="j-text-center">
              {voucher}
            </JimboText>
          </JimboStack>
          <JimboStack gap="xs" align="center">
            <JimboText size="micro" tone="grey">
              Boss
            </JimboText>
            <JamlBoss bossName={boss} scale={0.55} />
            <JimboText size="micro" className="j-text-center">
              {boss}
            </JimboText>
          </JimboStack>
          <JimboStack gap="xs" align="center">
            <JimboText size="micro" tone="grey">
              Tags
            </JimboText>
            <JimboRow gap="sm" align="end">
              <JimboStack gap="xs" align="center">
                <JamlTag tagName={smallTag} scale={0.55} />
                <JimboText size="micro" className="j-text-center">
                  {smallTag}
                </JimboText>
              </JimboStack>
              <JimboStack gap="xs" align="center">
                <JamlTag tagName={bigTag} scale={0.55} />
                <JimboText size="micro" className="j-text-center">
                  {bigTag}
                </JimboText>
              </JimboStack>
            </JimboRow>
          </JimboStack>
        </JimboBox>

        {activeAnte.shopItems.length > 0 ? (
          <JimboStack gap="xs" align="stretch">
            <div
              style={{
                display: "flex",
                flexWrap: "wrap",
                alignItems: "center",
                justifyContent: "space-between",
                gap: "0.4rem",
              }}
            >
              <JimboText size="sm" tone="white">
                Infinite Shop Queue ({activeAnte.shopItems.length.toLocaleString()} rolled — scroll right for more)
              </JimboText>
              {onLoadMoreShop ? (
                <div style={{ display: "flex", gap: "0.35rem", flexWrap: "wrap" }}>
                  <JimboButton
                    tone="blue"
                    size="xs"
                    onClick={() => onLoadMoreShop(50)}
                    title="Roll the next 50 shop cards from the current PRNG cursor"
                  >
                    +50 Shop Cards
                  </JimboButton>
                  <JimboButton
                    tone="orange"
                    size="xs"
                    onClick={() => onLoadMoreShop(200)}
                    title="Jump 200 shop cards deeper without re-rolling earlier slots"
                  >
                    +200 Deep
                  </JimboButton>
                </div>
              ) : null}
            </div>
            <JimboShopBelt onNearEnd={onLoadMoreShop ? () => onLoadMoreShop(50) : undefined}>
              {activeAnte.shopItems.map((item, idx) => (
                <ShopCard
                  key={idx}
                  item={item}
                  slotNumber={idx + 1}
                  watched={watchSet.has(normKey(MotelyItemType[item.type] ?? ""))}
                />
              ))}
            </JimboShopBelt>
          </JimboStack>
        ) : null}

        {activeAnte.packs.length > 0 ? (
          <JimboStack gap="sm" align="stretch">
            <JimboText size="sm" tone="white">
              Packs
            </JimboText>
            {activeAnte.packs.map((pack, idx) => (
              <PackRow key={idx} pack={pack} watchedKeys={watchSet} />
            ))}
          </JimboStack>
        ) : null}
      </JimboStack>
    </JimboPanel>
  );
}
