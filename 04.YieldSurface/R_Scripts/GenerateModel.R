library(DBI)
library(zoo)
library(e1071)
library(randomForest)
library(rpart)
library(parallel)

username = 'root'
password = 'password'
dbname1 = 'clo_universe'
dbname2 = 'clo_universe'
host = 'localhost'

con1 = dbConnect(RMariaDB::MariaDB(), user = username, password = password, dbname = dbname1, host = host)
con2 = dbConnect(RMariaDB::MariaDB(), user = username, password = password, dbname = dbname2, host = host)

#Rating = 'BB'
MVOC_Match_Expiry = 15
StartDate = LastDate - 50

dbSendQuery(con2, paste0("CREATE TEMPORARY TABLE PRICE_YIELD_RAW AS
(SELECT DATE(`ReceivedTime`) AS `REC_DATE`,`TrancheID`,`Manager`,`SuggestedPrice`,`CPNRATE`, `Yield(T2R)` AS `Yield`,`DM(T2R)` AS `DM`,`REDate`,`NAV`,`WAS`,`WAL` FROM ER.consolidatedprice 
WHERE `ModifiedRating` = '",Rating,"' AND `Yield(T2R)` IS NOT NULL AND `REGION` = 'US' AND YEAR(`REDATE`) > 2010 AND `CPNTYPE` = 'FLOAT' AND `RECEIVEDTIME` > '",StartDate-10,"') "))

res = dbSendQuery(con2, "SELECT `REC_DATE`,`TRANCHEID`,`Manager`,`REDate`,AVG(`SuggestedPrice`),AVG(`Yield`),AVG(`DM`), `CPNRATE`,`NAV`,`WAS`,`WAL` FROM PRICE_YIELD_RAW GROUP BY `REC_DATE`,`TrancheID`;")

Price_Yield = data.frame(dbFetch(res))
dbClearResult(res)

res = dbSendQuery(con1, paste0("SELECT `DATE` AS `REC_DATE`,CONCAT(MVOC.`CDONETNAME`,'.',MVOC.`LABEL`) AS `TRANCHEID`,`MVOC` FROM MVOC WHERE `DATE` > '", StartDate - 10 - MVOC_Match_Expiry, "';"))
MVOC_NEW = data.frame(dbFetch(res))
dbClearResult(res)


no_cores = 4
cl <<- makeCluster(no_cores, outfile = '')
clusterExport(cl, ls(.GlobalEnv))
Price_Yield$MVOC_Date = as.Date(parApply(cl,Price_Yield, 1, function(x) {
    iDealID = as.character(x['TRANCHEID'])
    iAsOfDate = x['REC_DATE']
    Match_SCope = MVOC_NEW[MVOC_NEW$TRANCHEID == iDealID,]
    Match_SCope = Match_SCope[Match_SCope$REC_DATE <= iAsOfDate,]
    Match_SCope = Match_SCope[Match_SCope$REC_DATE >= as.Date(iAsOfDate) - MVOC_Match_Expiry,]
    return(max(Match_SCope[, 'REC_DATE']))
}))
stopCluster(cl)

Yield_MVOC = merge(x = Price_Yield, y = MVOC_NEW, by.x = c('MVOC_Date', 'TRANCHEID'), by.y = c('REC_DATE', 'TRANCHEID'))
Yield_MVOC = Yield_MVOC[order(Yield_MVOC$REC_DATE),]

res = dbSendQuery(con1, paste0("SELECT * FROM CLOIE WHERE `TICKER` = (SELECT `TICKER` FROM cloie_type WHERE VINTAGE = 2 AND RATING = '",Rating,"' AND VALUETYPE = 'DM');"))
JPM_DM = data.frame(dbFetch(res))
dbClearResult(res)

Yield_MVOC$JPMDate = JPM_DM$Date[match(Yield_MVOC$REC_DATE, JPM_DM$Date)]
Yield_MVOC$JPMDate = na.locf(Yield_MVOC$JPMDate)
Yield_MVOC$JPM_DM = JPM_DM$Value[match(Yield_MVOC$JPMDate, JPM_DM$Date)]

##################################################################################
#                               Tidy Data
##################################################################################
{
    clusterExpiry = 4
    uniqueTrancheID = unique(Yield_MVOC[, c('TRANCHEID')])
    GroupingIDs = lapply(uniqueTrancheID, function(trancheid) {
        all_Dates = sort(Yield_MVOC$REC_DATE[Yield_MVOC$TRANCHEID == trancheid])
        allGPID = c(1, 1 * (all_Dates[-1] - all_Dates[-length(all_Dates)] >= clusterExpiry))
        output = data.frame(trancheid, all_Dates, ID = paste0(trancheid, '_', cumsum(allGPID)))
        return(output)
    })
    GroupingIDs = do.call('rbind', GroupingIDs)

    Yield_MVOC_Group = merge(x = Yield_MVOC, y = GroupingIDs, by.x = c('REC_DATE', 'TRANCHEID'), by.y = c('all_Dates', 'trancheid'))

    uniqueGroupIDs = unique(Yield_MVOC_Group[, 'ID'])

    GroupedStats = lapply(uniqueGroupIDs, function(groupID) {
        GroupData = Yield_MVOC_Group[Yield_MVOC_Group$ID == groupID,]
        Date = max(GroupData[, 'REC_DATE'])
        TrancheID = as.character(GroupData[1, 'TRANCHEID'])
        Mgr = GroupData[1, 'Manager']
        REDate = GroupData[1, 'REDate']
        Price = (GroupData[, 'AVG..SuggestedPrice..'] %*% as.numeric(GroupData[, 'REC_DATE'])) / sum(as.numeric(GroupData[, 'REC_DATE']))
        Yield = (GroupData[, 'AVG..Yield..'] %*% as.numeric(GroupData[, 'REC_DATE'])) / sum(as.numeric(GroupData[, 'REC_DATE']))
        DM = (GroupData[, 'AVG..DM..'] %*% as.numeric(GroupData[, 'REC_DATE'])) / sum(as.numeric(GroupData[, 'REC_DATE']))
        CpnRate = GroupData[1, 'CPNRATE']
        NAV = (GroupData[, 'NAV'] %*% as.numeric(GroupData[, 'REC_DATE'])) / sum(as.numeric(GroupData[, 'REC_DATE']))
        WAS = (GroupData[, 'WAS'] %*% as.numeric(GroupData[, 'REC_DATE'])) / sum(as.numeric(GroupData[, 'REC_DATE']))
        WAL = (GroupData[, 'WAL'] %*% as.numeric(GroupData[, 'REC_DATE'])) / sum(as.numeric(GroupData[, 'REC_DATE']))
        MVOC = (GroupData[, 'MVOC'] %*% as.numeric(GroupData[, 'REC_DATE'])) / sum(as.numeric(GroupData[, 'REC_DATE']))
        JPM_DM = (GroupData[, 'JPM_DM'] %*% as.numeric(GroupData[, 'REC_DATE'])) / sum(as.numeric(GroupData[, 'REC_DATE']))

        return(data.frame(Date, TrancheID, Mgr, REDate, Price, Yield, DM, CpnRate, NAV, WAS, WAL, MVOC, JPM_DM))
    })

    Data = do.call('rbind', GroupedStats)
    Data$T2R = pmax(0,as.numeric(Data$REDate - Data$Date) / 365.25)
    #Data$LifePremium = Data$T2R - Data$ModelLife

    col.Interest = c('DM', 'NAV', 'WAS','CpnRate', 'WAL', 'MVOC', 'JPM_DM', 'T2R')

}


##################################################################################
#                         Prepare data for training
##################################################################################
{
    #LastDate = Sys.Date() # as.Date('2018-02-01')
    T2R_Expiry = 0.5

    Data.Final = Data[Data$Date >= StartDate & Data$Date <= LastDate,]
    Data.Final = Data.Final[complete.cases(Data.Final[, col.Interest]),]
    Data.Interest = Data.Final[, col.Interest]
    Data.Final = Data.Final[mahalanobis(Data.Interest, colMeans(Data.Interest), cov(Data.Interest)) < 30,]
    Data.Final = Data.Final[Data.Final$T2R > T2R_Expiry,]
    N = nrow(Data.Final)
}


##################################################################################
#                         Un-constrainted Model Fitting
##################################################################################
{
    fmla = as.formula('DM ~ I(T2R^2) + T2R + CpnRate + JPM_DM + WAS + WAL + NAV + MVOC')
    LinearModel.DM = lm(fmla, data = Data.Final)
    SVM_Model.DM = svm(fmla, data = Data.Final)
    SVM_Model_Tune = tune(svm, train.x = fmla, data = Data.Final, kernel = "radial", ranges = list(cost = 10 ^ (-1:2), gamma = c(.5, 1, 2)))
    SVM.BestModel = SVM_Model_Tune$best.model
    D_Tree.DM = rpart(fmla, data = Data.Final)
    R_Forest.DM = randomForest(fmla, data = Data.Final)


    #cor(cbind(Data.Final$DM, LinearModel.DM$fitted.values, SVM.BestModel$fitted, predict(D_Tree.DM), R_Forest.DM$predicted, Ens.Model$fitted.values))
    #plot(x = Data.Final$T2R, y = Data.Final$DM, col = 'dodgerblue4', pch = 16)
    #points(x = Data.Final$T2R, y = LinearModel.DM$fitted.values, col = 'green', pch = 12)
    #points(x = Data.Final$T2R, y = SVM.BestModel$fitted, col = 'firebrick1', pch = 7)
    #points(x = Data.Final$T2R, y = predict(D_Tree.DM), col = 'darkorange', pch = 8)
    #points(x = Data.Final$T2R, y = Ens.Model$fitted.values)

    prediction = predict(R_Forest.DM, Data.Final)
    MAE = mean(abs(Data.Final$DM - prediction))
    MSE = sqrt(mean((Data.Final$DM - prediction) ^ 2))
    corr = cor(prediction, Data.Final$DM)

    #saveRDS(R_Forest.DM, file = paste0(format(LastDate, "%Y%m%d"), "_", Rating, ".rds"))
    #save(R_Forest.DM, file = 'Z:/32. Structuring/25. Mezzanine Analysis/03.Tools/04.YieldSurface/R_Model/20180713_BB.RData')
    #load('Z:/32. Structuring/25. Mezzanine Analysis/03.Tools/04.YieldSurface/R_Model/20180713_BB.RData')
}

