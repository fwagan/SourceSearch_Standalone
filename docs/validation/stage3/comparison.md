# Stage 3 UI comparison

Same snapshots; 36 groups per corpus; 5 warmups + 30 samples per group. Milliseconds. Threshold: P95 <= 100 ms for every group. These are sequential desktop measurements, not isolated causal profiling.

| Corpus | Before over 100ms | After over 100ms | Before max P95 | After max P95 | Before first query | After first query | Peak MiB before/after |
|---|---:|---:|---:|---:|---:|---:|---:|
| csharp | 29/36 | 31/36 | 388.81 | 782.46 | 170.82 | 242.17 | 542.7/610.5 |
| mixed | 33/36 | 33/36 | 236.80 | 604.34 | 158.00 | 363.75 | 803.1/946.7 |

## csharp

| Group | Before P50 | After P50 | Before P95 | After P95 | After target |
|---|---:|---:|---:|---:|---|
| q01-Contains | 90.21 | 223.54 | 369.64 | 782.46 | FAIL |
| q01-WholeWord | 70.12 | 172.81 | 134.08 | 200.18 | FAIL |
| q02-Contains | 51.19 | 166.40 | 133.19 | 188.49 | FAIL |
| q02-WholeWord | 40.83 | 168.20 | 109.00 | 188.25 | FAIL |
| q03-Contains | 34.65 | 162.73 | 105.44 | 187.06 | FAIL |
| q03-WholeWord | 29.46 | 171.25 | 107.16 | 208.92 | FAIL |
| q04-Contains | 29.20 | 166.03 | 96.44 | 223.35 | FAIL |
| q04-WholeWord | 30.77 | 174.21 | 103.40 | 192.13 | FAIL |
| q05-Contains | 28.17 | 170.80 | 96.63 | 192.77 | FAIL |
| q05-WholeWord | 32.91 | 169.36 | 102.80 | 192.12 | FAIL |
| q06-Contains | 32.41 | 162.68 | 108.15 | 188.62 | FAIL |
| q06-WholeWord | 33.75 | 166.03 | 105.36 | 182.89 | FAIL |
| q07-Contains | 34.16 | 168.10 | 94.49 | 224.99 | FAIL |
| q07-WholeWord | 33.86 | 165.16 | 101.88 | 194.59 | FAIL |
| q08-Contains | 32.92 | 167.18 | 99.33 | 257.47 | FAIL |
| q08-WholeWord | 32.38 | 166.50 | 105.83 | 197.34 | FAIL |
| q09-Contains | 36.26 | 168.11 | 109.19 | 192.52 | FAIL |
| q09-WholeWord | 34.70 | 169.71 | 110.15 | 191.60 | FAIL |
| q10-Contains | 35.22 | 174.84 | 113.79 | 214.05 | FAIL |
| q10-WholeWord | 34.23 | 166.24 | 98.59 | 192.97 | FAIL |
| q11-Contains | 36.84 | 182.27 | 105.64 | 210.27 | FAIL |
| q11-WholeWord | 36.97 | 189.56 | 116.88 | 207.43 | FAIL |
| q12-Contains | 32.91 | 183.75 | 102.60 | 245.33 | FAIL |
| q12-WholeWord | 39.62 | 187.42 | 117.76 | 214.81 | FAIL |
| q13-Contains | 31.08 | 172.22 | 107.27 | 211.74 | FAIL |
| q13-WholeWord | 2.28 | 4.58 | 233.48 | 17.57 | pass |
| q14-Contains | 34.99 | 176.20 | 167.24 | 216.57 | FAIL |
| q14-WholeWord | 32.61 | 183.84 | 109.88 | 303.45 | FAIL |
| q15-Contains | 33.57 | 197.43 | 107.33 | 264.18 | FAIL |
| q15-WholeWord | 3.05 | 7.00 | 156.31 | 66.91 | pass |
| q16-Contains | 1.79 | 6.02 | 84.91 | 8.13 | pass |
| q16-WholeWord | 3.80 | 5.55 | 137.03 | 32.08 | pass |
| filter-include-cs | 39.38 | 176.81 | 125.84 | 197.30 | FAIL |
| filter-exclude-cs | 40.56 | 56.39 | 202.67 | 140.23 | FAIL |
| filter-js-json | 1.16 | 8.01 | 92.55 | 77.23 | pass |
| filter-overlap | 106.11 | 69.17 | 388.81 | 132.66 | FAIL |

## mixed

| Group | Before P50 | After P50 | Before P95 | After P95 | After target |
|---|---:|---:|---:|---:|---|
| q01-Contains | 77.81 | 305.67 | 171.11 | 604.34 | FAIL |
| q01-WholeWord | 68.66 | 244.00 | 134.86 | 296.32 | FAIL |
| q02-Contains | 55.59 | 243.88 | 121.42 | 397.01 | FAIL |
| q02-WholeWord | 51.01 | 177.83 | 218.23 | 201.96 | FAIL |
| q03-Contains | 40.39 | 196.60 | 107.24 | 228.99 | FAIL |
| q03-WholeWord | 34.15 | 207.97 | 91.99 | 461.83 | FAIL |
| q04-Contains | 32.19 | 184.23 | 112.43 | 221.38 | FAIL |
| q04-WholeWord | 38.17 | 189.54 | 94.64 | 219.19 | FAIL |
| q05-Contains | 36.98 | 191.46 | 180.67 | 229.47 | FAIL |
| q05-WholeWord | 38.36 | 236.50 | 107.27 | 331.27 | FAIL |
| q06-Contains | 35.06 | 173.47 | 100.24 | 235.86 | FAIL |
| q06-WholeWord | 36.32 | 170.85 | 236.80 | 224.43 | FAIL |
| q07-Contains | 37.10 | 170.93 | 146.64 | 190.59 | FAIL |
| q07-WholeWord | 35.29 | 170.69 | 127.12 | 212.69 | FAIL |
| q08-Contains | 35.48 | 184.94 | 111.82 | 236.83 | FAIL |
| q08-WholeWord | 35.42 | 165.25 | 103.41 | 196.47 | FAIL |
| q09-Contains | 38.17 | 191.16 | 107.92 | 327.00 | FAIL |
| q09-WholeWord | 35.30 | 183.15 | 104.64 | 250.12 | FAIL |
| q10-Contains | 37.46 | 174.60 | 106.78 | 207.71 | FAIL |
| q10-WholeWord | 36.52 | 171.70 | 120.88 | 199.76 | FAIL |
| q11-Contains | 39.56 | 192.56 | 108.61 | 257.84 | FAIL |
| q11-WholeWord | 45.04 | 210.46 | 133.75 | 228.20 | FAIL |
| q12-Contains | 39.14 | 196.94 | 113.10 | 266.64 | FAIL |
| q12-WholeWord | 47.03 | 227.18 | 126.82 | 281.82 | FAIL |
| q13-Contains | 39.35 | 180.84 | 113.34 | 215.83 | FAIL |
| q13-WholeWord | 119.52 | 259.32 | 183.65 | 288.54 | FAIL |
| q14-Contains | 80.08 | 193.10 | 132.69 | 214.77 | FAIL |
| q14-WholeWord | 36.57 | 173.58 | 103.49 | 198.04 | FAIL |
| q15-Contains | 81.34 | 195.65 | 153.67 | 225.06 | FAIL |
| q15-WholeWord | 3.57 | 5.78 | 219.63 | 8.54 | pass |
| q16-Contains | 2.74 | 5.84 | 149.31 | 35.60 | pass |
| q16-WholeWord | 1.21 | 4.54 | 5.22 | 16.23 | pass |
| filter-include-cs | 33.06 | 174.28 | 103.16 | 196.82 | FAIL |
| filter-exclude-cs | 38.24 | 197.25 | 104.44 | 252.58 | FAIL |
| filter-js-json | 37.63 | 181.76 | 111.53 | 261.69 | FAIL |
| filter-overlap | 37.35 | 178.91 | 115.76 | 204.80 | FAIL |
