import { simulateSaleRound, weightedAvgCost } from './business-sim';

describe('simulateSaleRound', () => {
  it('sells expected demand at the reference price', () => {
    const [line] = simulateSaleRound(1, [{ productCode: 'LEMONADE', quantity: 20, avgUnitCost: 5, sellPrice: 10 }]);
    expect(line).toMatchObject({ demand: 8, unitsSold: 8, revenue: 80, costOfGoods: 40, profit: 40, stockLeft: 12, tips: ['HEALTHY_PROFIT'] });
  });

  it('never sells more than the stock and flags missed sales', () => {
    const [line] = simulateSaleRound(1, [{ productCode: 'LEMONADE', quantity: 3, avgUnitCost: 5, sellPrice: 10 }]);
    expect(line!.unitsSold).toBe(3);
    expect(line!.tips).toContain('SOLD_OUT_MISSED_SALES');
  });

  it('caps sales at the level capacity', () => {
    const [line] = simulateSaleRound(1, [{ productCode: 'LEMONADE', quantity: 50, avgUnitCost: 5, sellPrice: 6 }]);
    expect(line!.demand).toBeGreaterThan(10);
    expect(line!.unitsSold).toBe(10);
  });

  it('higher price -> lower demand; explains it', () => {
    const cheap = simulateSaleRound(1, [{ productCode: 'BREAD', quantity: 50, avgUnitCost: 8, sellPrice: 15 }])[0]!;
    const pricey = simulateSaleRound(1, [{ productCode: 'BREAD', quantity: 50, avgUnitCost: 8, sellPrice: 30 }])[0]!;
    expect(pricey.demand).toBeLessThan(cheap.demand);
    expect(pricey.tips).toContain('PRICE_TOO_HIGH');
  });

  it('warns when selling below cost', () => {
    const [line] = simulateSaleRound(1, [{ productCode: 'FLOWER', quantity: 10, avgUnitCost: 20, sellPrice: 15 }]);
    expect(line!.profit).toBeLessThan(0);
    expect(line!.tips).toContain('SELLING_BELOW_COST');
  });

  it('higher shop level sells more', () => {
    const l1 = simulateSaleRound(1, [{ productCode: 'LEMONADE', quantity: 100, avgUnitCost: 5, sellPrice: 10 }])[0]!;
    const l5 = simulateSaleRound(5, [{ productCode: 'LEMONADE', quantity: 100, avgUnitCost: 5, sellPrice: 10 }])[0]!;
    expect(l5.unitsSold).toBeGreaterThan(l1.unitsSold);
  });
});

describe('weightedAvgCost', () => {
  it('averages by quantity', () => {
    expect(weightedAvgCost(10, 5, 10, 7)).toBe(6);
    expect(weightedAvgCost(0, 0, 5, 12)).toBe(12);
    expect(weightedAvgCost(0, 0, 0, 12)).toBe(0);
  });
});
