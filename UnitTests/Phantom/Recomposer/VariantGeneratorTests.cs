﻿using System.Collections.Generic;
using System.Linq;
using CacheUtils.TranscriptCache;
using Moq;
using Phantom.PositionCollections;
using Phantom.Recomposer;
using UnitTests.TestDataStructures;
using UnitTests.TestUtilities;
using VariantAnnotation.Interface.Positions;
using VariantAnnotation.Interface.Providers;
using Vcf.VariantCreator;
using Xunit;

namespace UnitTests.Phantom.Recomposer
{
    public sealed class VariantGeneratorTests
    {
        private readonly VariantId _vidCreator = new VariantId();
        
        [Fact]
        public void GetPositionsAndRefAltAlleles_AsExpected()
        {
            var genotypeBlock = new GenotypeBlock(new[] { "1|2", "1/1", "0|1", "0/1" }.Select(Genotype.GetGenotype).ToArray());
            var genotypeToSample =
                new Dictionary<GenotypeBlock, List<int>> { { genotypeBlock, new List<int> { 0 } } };
            var indexOfUnsupportedVars = Enumerable.Repeat(new HashSet<int>(), genotypeBlock.Genotypes.Length).ToArray();
            var starts = new[] { 356, 358, 360, 361 };
            var functionBlockRanges = new List<int> { 358, 360, 362, 364 };
            var alleles = new[] { new[] { "G", "C", "T" }, new[] { "A", "T" }, new[] { "C", "G" }, new[] { "G", "T" } };
            const string refSequence = "GAATCG";
            var alleleBlockToSampleHaplotype = AlleleBlock.GetAlleleBlockToSampleHaplotype(genotypeToSample, indexOfUnsupportedVars, starts, functionBlockRanges, out var alleleBlockGraph);
            var mergedAlleleBlockToSampleHaplotype =
                AlleleBlockMerger.Merge(alleleBlockToSampleHaplotype, alleleBlockGraph).ToArray();
            var alleleSet = new AlleleSet(ChromosomeUtilities.Chr1, starts, alleles);
            var alleleBlocks = mergedAlleleBlockToSampleHaplotype.Select(x => x.Key).ToArray();
            var sequence = new NSequence();

            var result1 = VariantGenerator.GetPositionsAndRefAltAlleles(alleleBlocks[0], alleleSet, refSequence, starts[0], null, sequence, _vidCreator);
            var result2 = VariantGenerator.GetPositionsAndRefAltAlleles(alleleBlocks[1], alleleSet, refSequence, starts[0], null, sequence, _vidCreator);

            var expectedVarPosIndexes1 = new List<int> { 0, 1 };
            var expectedVarPosIndexes2 = new List<int> { 0, 1, 2 };

            Assert.Equal((356, 360, "GAATC", "CATTC"), (result1.Start, result1.End, result1.Ref, result1.Alt));
            for (var i = 0; i < expectedVarPosIndexes1.Count; i++) Assert.Equal(expectedVarPosIndexes1[i], result1.VarPosIndexesInAlleleBlock[i]);

            Assert.Equal((356, 360, "GAATC", "TATTG"), (result2.Start, result2.End, result2.Ref, result2.Alt));
            for (var i = 0; i < expectedVarPosIndexes2.Count; i++) Assert.Equal(expectedVarPosIndexes2[i], result2.VarPosIndexesInAlleleBlock[i]);
        }


        [Fact]
        public void VariantGenerator_AsExpected()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T,G	.	PASS	.	GT:PS	0|1:123	2/2:789	0|2:456", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A,G	.	PASS	.	GT:PS	1|1:301	1|2:789	1|2:456", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	.	PASS	.	GT:PS	.	1|0:789	0/1:.", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 4, 6, 8 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Equal(2, recomposedPositions.Count);
            Assert.Equal("chr1	2	.	AGC	AGA,GGG,TGA	.	PASS	RECOMPOSED	GT:PS	1|3:123	.	1|2:456", string.Join("\t", recomposedPositions[0].VcfFields));
            Assert.Equal("chr1	2	.	AGCTG	GGATC,GGGTG	.	PASS	RECOMPOSED	GT:PS	.	1|2:789	.", string.Join("\t", recomposedPositions[1].VcfFields));

            //Check LinkedVids
            //SNVs
            Assert.Equal(2, position1.LinkedVids.Length);
            Assert.Equal(new List<string> { "1-2-AGC-TGA" }, position1.LinkedVids[0]);
            Assert.Equal(new List<string> { "1-2-AGCTG-GGATC", "1-2-AGC-GGG" }, position1.LinkedVids[1]);

            Assert.Equal(2, position2.LinkedVids.Length);
            Assert.Equal(new List<string> { "1-2-AGCTG-GGATC", "1-4-C-A", "1-2-AGC-TGA" }, position2.LinkedVids[0]);
            Assert.Equal(new List<string> { "1-2-AGC-GGG" }, position2.LinkedVids[1]);

            Assert.Single(position3.LinkedVids);
            Assert.Equal(new List<string> { "1-2-AGCTG-GGATC" }, position3.LinkedVids[0]);

            //MNVs
            Assert.Equal(3, recomposedPositions[0].LinkedVids.Length);
            Assert.Equal(new List<string> { "1-4-C-A" }, recomposedPositions[0].LinkedVids[0]);
            Assert.Equal(new List<string> { "1-2-A-G", "1-4-C-G" }, recomposedPositions[0].LinkedVids[1]);
            Assert.Equal(new List<string> { "1-2-A-T", "1-4-C-A" }, recomposedPositions[0].LinkedVids[2]);
            Assert.Equal(2, recomposedPositions[1].LinkedVids.Length);
            Assert.Equal(new List<string> { "1-2-A-G", "1-4-C-A", "1-6-G-C" }, recomposedPositions[1].LinkedVids[0]);
            Assert.Equal(new List<string> { "1-2-A-G", "1-4-C-G" }, recomposedPositions[1].LinkedVids[1]);
        }

        [Fact]
        public void VariantGenerator_HomoReferenceGenotype_Output()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	1	.	C	A	.	PASS	.	GT:PS	0|1:1584593	0/0:.", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	C	.	PASS	.	GT:PS	0|1:1584593	0/0:.", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 3, 4 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("chr1	1	.	CA	AC	.	PASS	RECOMPOSED	GT:PS	0|1:1584593	0|0", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_NoMnvAfterTrimming_NotRecompose()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	1	.	C	A	.	PASS	.	GT:PS	1|0:1584593	1|1:.	0|1:.", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	C	.	PASS	.	GT:PS	0|1:1584593	0/0:.	0/0:.", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 3, 4 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2 }, functionBlockRanges).ToList();

            Assert.Empty(recomposedPositions);
        }

        [Fact]
        public void VariantGenerator_ConflictAltAlleles_NoRecomposition()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAATCGCGA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	1	.	C	T	.	PASS	.	GT	0/1	1|0", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T	.	PASS	.	GT	1|1	0/0", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	G	.	PASS	.	GT	0|1	0|1", sequenceProvider.RefNameToChromosome);
            var position4 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A	.	PASS	.	GT	1|1	0/0", sequenceProvider.RefNameToChromosome);

            var functionBlockRanges = new List<int> { 3, 4, 6, 6 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3, position4 }, functionBlockRanges).ToList();

            Assert.Empty(recomposedPositions);
        }


        [Fact]
        public void VariantGenerator_ConflictAltAlleles_AlleleBlockStartInTheMiddle_NoRecompositionNoException()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAATCGCGA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	1	.	C	T	.	PASS	.	GT	0/1	0|1", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T	.	PASS	.	GT	0/1	0|0", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	G	.	PASS	.	GT	1|1	1|1", sequenceProvider.RefNameToChromosome);
            var position4 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A	.	PASS	.	GT	1|1	0|1", sequenceProvider.RefNameToChromosome);

            var functionBlockRanges = new List<int> { 3, 4, 6, 6 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3, position4 }, functionBlockRanges).ToList();

            Assert.Empty(recomposedPositions);
        }

        [Fact]
        public void VariantGenerator_ForceGenotype_ConsistentAllele_Recompose()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAATCGCGA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T	.	PASS	.	GT	1|1	0/0", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	G	.	PASS	.	GT	0|1	0|1", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	G,A	.	PASS	.	GT	0|1	0/0", sequenceProvider.RefNameToChromosome);

            var functionBlockRanges = new List<int> { 4, 6, 6 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("chr1	2	.	AGC	TGC,TGG	.	PASS	RECOMPOSED	GT	1|2	.", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_OverlappingDeletionInTheMiddle_Ignored()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAATCGCGA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T	.	PASS	.	GT	0|1	0/0", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	CTGAATCGCGA	C	.	PASS	.	GT	0/0	0|1", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A	.	PASS	.	GT	1|1	0/0", sequenceProvider.RefNameToChromosome);

            var functionBlockRanges = new List<int> { 4, 6, 6 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("chr1	2	.	AGC	AGA,TGA	.	PASS	RECOMPOSED	GT	1|2	.", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_OverlappingInsertionInTheMiddle_Ignored()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("TACAGGGTTTCCC"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("1\t2\t.\tA\tC\t153\tPASS\tSNVHPOL=4;MQ=58\tGT:GQ:GQX:DP:DPF:AD:ADF:ADR:SB:FT:PL:RPS:ME:DQ\t1|0:119:7:30:9:20,10:20,8:0,2:-13.6:PASS:121,0,187:240982163:0:0\t1|0:100:11:36:9:27,9:22,4:5,5:-4.8:PASS:102,0,300:.:.:.\t0|0:83:83:37:20:36,1:26,0:10,1:0:PASS:0,82,370:.:.:.", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("1\t3\t.\tC\tT\t56\tPASS\tSNVHPOL=3;MQ=58\tGT:GQ:GQX:DP:DPF:AD:ADF:ADR:SB:FT:PL:RPS:ME:DQ\t1|0:65:9:31:8:24,6:22,4:2,2:-4.1:PASS:66,0,274:240982163:0:.\t1|0:57:3:35:11:29,6:23,2:6,4:1.6:LowGQX:59,0,334:.:.:.\t0|0:111:111:38:20:38,0:26,0:12,0:0:PASS:0,114,370:.:.:.", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("1\t3\t.\tC\tCGTGGGTGAAGAGCCGTGGGTGAAGGGCT\t75\tPASS\tCIGAR=1M28I;RU=.;REFREP=0;IDREP=1;MQ=58\tGT:GQ:GQX:DPI:AD:ADF:ADR:FT:PL:RPS:ME:DQ\t0|1:118:5:39:19,10:14,9:5,1:PASS:115,0,303:240982163:0:.\t0|0:142:142:46:43,0:28,0:15,0:PASS:0,145,880:.:.:.\t1|0:25:0:58:42,8:23,5:19,3:LowGQX:22,0,699:.:.:.", sequenceProvider.RefNameToChromosome);
            var position4 = AnnotationUtilities.GetSimplePosition("1\t4\t.\tA\tG\t649\tPASS\tSNVHPOL=2;MQ=59\tGT:GQ:GQX:DP:DPF:AD:ADF:ADR:SB:FT:PL:RPS:ME:DQ\t1|1:90:13:31:7:0,31:0,28:0,3:-19.9:PASS:370,93,0:.:0:0\t1|0:179:14:39:7:22,17:18,10:4,7:-19.8:PASS:181,0,231:240982173:.:.\t1|0:114:10:33:25:21,12:17,6:4,6:-9:PASS:116,0,240:240982171:.:.", sequenceProvider.RefNameToChromosome);

            var functionBlockRanges = new List<int> { 4, 5, 5, 6 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3, position4 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("1\t2\t.\tAC\tCT\t56\tPASS\tRECOMPOSED\tGT:GQ\t1|0:65\t.\t0|0:83", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_OverlappingDeletionAtTheEnd_Ignored()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAATCGCGA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T	.	PASS	.	GT	0|1	0/0", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A	.	PASS	.	GT	1|1	0/0", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	4	.	CTGAATCGCGA	C	.	PASS	.	GT	0/0	0|1", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 4, 6, 6 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("chr1	2	.	AGC	AGA,TGA	.	PASS	RECOMPOSED	GT	1|2	0|0", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_FilterTag_DotTreatedAsPass()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T,G	.	PASS	.	GT:PS	0|1:123	2/2:.	0|2:456", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A,G	.	.	.	GT:PS	1|1:301	1|2:.	1|2:456", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	.	FailedForSomeReason	.	GT:PS	.	1|0:.	0/1:456", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 4, 6, 8 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Equal(2, recomposedPositions.Count);
            Assert.Equal("chr1	2	.	AGC	AGA,GGG,TGA	.	PASS	RECOMPOSED	GT:PS	1|3:123	.	1|2:456", string.Join("\t", recomposedPositions[0].VcfFields));
            Assert.Equal("chr1	2	.	AGCTG	GGATC,GGGTG	.	FilteredVariantsRecomposed	RECOMPOSED	GT	.	1|2	.", string.Join("\t", recomposedPositions[1].VcfFields));
        }

        [Fact]
        public void VariantGenerator_FilterTag_OnlyDecomposedVariantsConsidered()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T	.	PASS	.	GT	0|1	0/1	0|0", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A	.	FailedForSomeReason	.	GT	0|0	0/1	0|0", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	.	PASS	.	GT	1|1	0/1	0|0", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 6, 8, 10 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("chr1	2	.	AGCTG	AGCTC,TGCTC	.	PASS	RECOMPOSED	GT	1|2	.	0|0", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_FilterTag_PassedMnvOverridesFailedOne()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T	.	PASS	.	GT	0|1	0|1", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A	.	FailedForSomeReason	.	GT	0|0	0|1", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	.	PASS	.	GT	0|1	0|1", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 6, 8, 10 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("chr1	2	.	AGCTG	TGATC,TGCTC	.	PASS	RECOMPOSED	GT	0|2	0|1", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_MinQualUsed_DotIgnored()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T,G	.	PASS	.	GT:PS	0|1:123	2/2:.	0|2:456", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A,G	45	PASS	.	GT:PS	1|1:301	1|2:.	1|2:456", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	30.1	PASS	.	GT	.	1|0	0/1", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 4, 6, 8 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Equal(2, recomposedPositions.Count);
            Assert.Equal("chr1	2	.	AGC	AGA,GGG,TGA	45	PASS	RECOMPOSED	GT:PS	1|3:123	.	1|2:456", string.Join("\t", recomposedPositions[0].VcfFields));
            Assert.Equal("chr1	2	.	AGCTG	GGATC,GGGTG	30.1	PASS	RECOMPOSED	GT	.	1|2	.", string.Join("\t", recomposedPositions[1].VcfFields));
        }

        [Fact]
        public void VariantGenerator_MinGQUsed_DotAndNullIgnored()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T,G	.	PASS	.	GT:PS:GQ	0|1:123:.	2/2:.:14.2	0|2:456:.", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A,G	.	PASS	.	GT:PS:GQ	1|1:301:.	1|2:.:18	1|2:456:15.6", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	.	PASS	.	GT	.	1|0	0/1", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 4, 6, 8 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Equal(2, recomposedPositions.Count);
            Assert.Equal("chr1	2	.	AGC	AGA,GGG,TGA	.	PASS	RECOMPOSED	GT:GQ:PS	1|3:.:123	.	1|2:15.6:456", string.Join("\t", recomposedPositions[0].VcfFields));
            Assert.Equal("chr1	2	.	AGCTG	GGATC,GGGTG	.	PASS	RECOMPOSED	GT:GQ	.	1|2:14.2	.", string.Join("\t", recomposedPositions[1].VcfFields));
        }

        [Fact]
        public void VariantGenerator_SampleColumnCorrectlyProcessed_WhenTrailingMissingValuesDroped()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T,G	.	PASS	.	GT:PS:GQ	0|1:123	2/2:.:14.2	./.", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A,G	.	PASS	.	GT:PS:GQ	./.	1|2:.:18	1|2:456:15.6", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	.	PASS	.	GT	./.	1|0	./.", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 4, 6, 8 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("chr1	2	.	AGCTG	GGATC,GGGTG	.	PASS	RECOMPOSED	GT:GQ	.	1|2:14.2	.", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_AllTrailingMissingValuesDropped()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T,G	.	PASS	.	GT:GQ:PS	0|1:.:123	2/2	1|1:17:456", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A,G	.	PASS	.	GT:GQ:PS	./.	1|2	1|2:15.6:456", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	.	PASS	.	GT:GQ:PS	./.	1|0	1|1:13:456", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 4, 6, 8 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("chr1	2	.	AGCTG	GGATC,GGGTG,TGATC,TGGTC	.	PASS	RECOMPOSED	GT:GQ:PS	.	1|2	3|4:13:456", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_RefAllelesAddedToMergeAlleles()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAACT"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T	.	PASS	.	GT	0|1	0|0	0|1	0|0", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A	.	PASS	.	GT	1|1	1|1	1|1	1|1", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	.	PASS	.	GT	1|1	1|1	1|1	1|1", sequenceProvider.RefNameToChromosome);
            var position4 = AnnotationUtilities.GetSimplePosition("chr1	8	.	A	G	.	PASS	.	GT	0|1	0|1	0|0	0|0", sequenceProvider.RefNameToChromosome);
            var functionBlockRanges = new List<int> { 4, 6, 8, 10 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3, position4 }, functionBlockRanges).ToList();

            Assert.Single(recomposedPositions);
            Assert.Equal("chr1	2	.	AGCTGAA	AGATCAA,AGATCAG,TGATCAA,TGATCAG	.	PASS	RECOMPOSED	GT	1|4	1|2	1|3	1|1", string.Join("\t", recomposedPositions[0].VcfFields));
        }

        [Fact]
        public void VariantGenerator_HomozygousSitesAndPhasedSites_Recomposed()
        {
            var mockSequenceProvider = new Mock<ISequenceProvider>();
            mockSequenceProvider.SetupGet(x => x.RefNameToChromosome)
                .Returns(ChromosomeUtilities.RefNameToChromosome);
            mockSequenceProvider.SetupGet(x => x.Sequence).Returns(new SimpleSequence("CAGCTGAA"));
            var sequenceProvider = mockSequenceProvider.Object;

            var position1 = AnnotationUtilities.GetSimplePosition("chr1	2	.	A	T	.	PASS	.	GT	1/1	1/1	1/1", sequenceProvider.RefNameToChromosome);
            var position2 = AnnotationUtilities.GetSimplePosition("chr1	3	.	G	A,G	45	PASS	.	GT:PS	1|1:2	1|2:2	1|2:2", sequenceProvider.RefNameToChromosome);
            var position3 = AnnotationUtilities.GetSimplePosition("chr1	4	.	C	A	45	PASS	.	GT	1/1	1/1	1/1", sequenceProvider.RefNameToChromosome);
            var position4 = AnnotationUtilities.GetSimplePosition("chr1	5	.	T	A,G	45	PASS	.	GT:PS	1|1:4	1|2:2	1|2:4", sequenceProvider.RefNameToChromosome);
            var position5 = AnnotationUtilities.GetSimplePosition("chr1	6	.	G	C	30	PASS	.	GT	1/1	1/1	1/1", sequenceProvider.RefNameToChromosome);

            var functionBlockRanges = new List<int> { 4, 5, 6, 7, 8 };

            var recomposer = new VariantGenerator(sequenceProvider, _vidCreator);
            var recomposedPositions = recomposer.Recompose(new List<ISimplePosition> { position1, position2, position3, position4, position5 }, functionBlockRanges).ToList();

            Assert.Equal(3, recomposedPositions.Count);
            Assert.Equal("chr1	2	.	AGC	TAA,TGA	45	PASS	RECOMPOSED	GT:PS	.	.	1|2:2", string.Join("\t", recomposedPositions[0].VcfFields));
            Assert.Equal("chr1	2	.	AGCTG	TAAAC,TGAGC	30	PASS	RECOMPOSED	GT:PS	1|1	1|2:2	.", string.Join("\t", recomposedPositions[1].VcfFields));
            Assert.Equal("chr1	4	.	CTG	AAC,AGC	30	PASS	RECOMPOSED	GT:PS	.	.	1|2:4", string.Join("\t", recomposedPositions[2].VcfFields));
        }
    }
}