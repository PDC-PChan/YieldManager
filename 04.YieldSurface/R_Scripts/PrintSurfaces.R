#load('Z:/32. Structuring/25. Mezzanine Analysis/03.Tools/04.YieldSurface/R_Model/20180715_BB.RData')
#Data.Final = read.csv('Z:/32. Structuring/25. Mezzanine Analysis/03.Tools/04.YieldSurface/SurfaceTrainingData/20180714_BB.csv',header = T)
#Rating = 'BB'

MVOC_UL = round(range(Data.Final$MVOC), -1)
CpnRate_UL = round(range(Data.Final$CpnRate), 1)

Range_MVOC = seq(MVOC_UL[1], MVOC_UL[2], 1)
Range_CpnRate = seq(CpnRate_UL[1], CpnRate_UL[2], 0.5)
Range_T2R = seq(0, 5, 0.5)

iWAS = mean(Data.Final$WAS)
iWAL = mean(Data.Final$WAL)
iJPM_DM = mean(Data.Final$JPM_DM)
iNAV = mean(Data.Final$NAV)

All.Surfaces = lapply(Range_CpnRate, function(Cpn) {
    data.frame.Matrix = outer(Range_T2R, Range_MVOC, function(iT2R, iMVOC) {
        theBond = data.frame(T2R = iT2R, MVOC = iMVOC, CpnRate = Cpn, WAL = iWAL, WAS = iWAS, JPM_DM = iJPM_DM, NAV = iNAV)
        prediction = predict(MyModel, newdata = theBond)
        return(as.numeric(prediction))
    })
})
names(All.Surfaces) = as.character(Range_CpnRate)

#sink('Z:/32. Structuring/25. Mezzanine Analysis/03.Tools/04.YieldSurface/YieldSurfaceOutput/test.csv', type = 'output')
#lapply(names(All.Surfaces), function(CpnRate) {print(CpnRate) write.csv(All.Surfaces[[CpnRate]])})
#sink()