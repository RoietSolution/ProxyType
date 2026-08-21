/* ProxyType legacy dashboard service catalog alignment. Catalog entries only;
   workflows remain unavailable until their individual implementation phase. */
SET NOCOUNT ON;

MERGE catalog.ServiceCategories AS target
USING (VALUES ('OTHER','Other Services',90)) AS source(Code, Name, SortOrder)
ON target.Code = source.Code
WHEN NOT MATCHED THEN INSERT(Code, Name, SortOrder) VALUES(source.Code, source.Name, source.SortOrder);

DECLARE @Services TABLE
(
    CategoryCode nvarchar(50) NOT NULL,
    Code nvarchar(60) NOT NULL,
    Name nvarchar(150) NOT NULL,
    Description nvarchar(500) NULL,
    Chargeable bit NOT NULL,
    Commissionable bit NOT NULL,
    RequiresKyc bit NOT NULL,
    SortOrder int NOT NULL
);

INSERT @Services VALUES
('WALLET','wallet_transfer','Wallet to Wallet','Legacy dashboard wallet transfer entry.',1,0,0,10),
('WALLET','fund_request','Fund Request','Legacy dashboard wallet funding entry.',0,0,0,20),
('RECHARGE','recharge_v1','Mobile Recharge 3','Legacy dashboard Recharge V3 mobile entry.',0,1,1,10),
('RECHARGE','recharge_v2','Mobile Recharge 2','Legacy dashboard Recharge V4 mobile entry.',0,1,1,20),
('RECHARGE','recharge_v3','DTH Recharge','Legacy dashboard Recharge V3 DTH entry.',0,1,1,30),
('RECHARGE','recharge_v4','Mobile Recharge 5','Legacy dashboard Recharge V5 mobile entry.',0,1,1,40),
('RECHARGE','recharge_v5','DTH Recharge (V5)','Legacy dashboard Recharge V5 DTH entry.',0,1,1,50),
('BILL','bbps','Bill Payment','Legacy dashboard BBPS entry.',0,1,1,10),
('BILL','bbps_v2','Bill Payment 2','Legacy dashboard BBPS second entry.',0,1,1,20),
('BILL','bbps_v3','Bill Payment 3','Legacy dynamic-provider bill payment entry.',0,1,1,30),
('TRANSFER','dmt','Domestic Money Transfer','Shared DMT capability; provider-specific variants remain separate.',1,0,1,10),
('TRANSFER','dmt_v2','Domestic Money Transfer 2','Legacy DMT variant entry.',1,0,1,20),
('TRANSFER','payout','Payout','Legacy dashboard payout entry.',1,0,1,30),
('TRANSFER','payout_v2','Payout 2','Legacy dashboard payout variant.',1,0,1,40),
('AEPS','aeps','AEPS','Legacy dashboard AEPS entry.',0,1,1,10),
('AEPS','aeps_v2','AEPS 2.0','Legacy dashboard AEPS 2.0 entry.',0,1,1,20),
('AEPS','aeps_union_bank','AEPS Union Bank','Legacy provider-specific AEPS entry.',0,1,1,30),
('GOVERNMENT','pan_card','Pan NSDL','Legacy dashboard PAN NSDL entry.',1,0,1,10),
('BANKING','open_account','Bank Account Open','Legacy dashboard bank account opening group.',0,0,1,10),
('COMMERCE','product','Products','Legacy dashboard products entry.',0,0,0,10),
('COMMERCE','credit_card_external','Credit Card','Legacy dashboard external credit-card entry.',0,0,1,20),
('OTHER','financial_products','Financial Products','Legacy dashboard catalog-only entry.',0,0,0,10),
('OTHER','investment_products','Investment Products','Legacy dashboard catalog-only entry.',0,0,0,20),
('OTHER','government_service','Government Service','Legacy dashboard catalog-only entry.',0,0,0,30),
('OTHER','saving_account','Saving Account','Legacy dashboard catalog-only entry.',0,0,1,40),
('OTHER','demat_account','Demat Account','Legacy dashboard catalog-only entry.',0,0,1,50),
('OTHER','personal_loan','Personal Loan','Legacy dashboard catalog-only entry.',0,0,1,60),
('OTHER','pay_later','Pay Later','Legacy dashboard catalog-only entry.',0,0,1,70),
('OTHER','gold_loan','Gold Loan','Legacy dashboard catalog-only entry.',0,0,1,80),
('OTHER','business_account','Business Account','Legacy dashboard catalog-only entry.',0,0,1,90),
('AEPS','aeps_wallet_transfer','Aeps Wallet to Wallet','Legacy dashboard AEPS wallet transfer entry.',1,0,1,40);

MERGE catalog.Services AS target
USING
(
    SELECT c.ServiceCategoryId, s.Code, s.Name, s.Description, s.Chargeable, s.Commissionable, s.RequiresKyc, s.SortOrder
    FROM @Services s INNER JOIN catalog.ServiceCategories c ON c.Code = s.CategoryCode
) AS source
ON target.Code = source.Code
WHEN MATCHED THEN UPDATE SET
    Name = source.Name,
    Description = source.Description,
    IsChargeable = source.Chargeable,
    IsCommissionable = source.Commissionable,
    RequiresKyc = source.RequiresKyc,
    SortOrder = source.SortOrder,
    UpdatedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ServiceCategoryId, Code, Name, Description, IsActive, IsChargeable, IsCommissionable, RequiresKyc, SortOrder)
    VALUES(source.ServiceCategoryId, source.Code, source.Name, source.Description, 1, source.Chargeable, source.Commissionable, source.RequiresKyc, source.SortOrder);

DECLARE @Platform uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE Code = 'PLATFORM');
INSERT catalog.OrganizationServicePermissions(OrganizationUnitId, ServiceId, Effect, Reason)
SELECT @Platform, s.ServiceId, 'ALLOW', 'Legacy dashboard catalog alignment; workflow remains subject to implementation and authorization.'
FROM catalog.Services s
INNER JOIN @Services seed ON seed.Code = s.Code
WHERE @Platform IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1 FROM catalog.OrganizationServicePermissions p
      WHERE p.OrganizationUnitId = @Platform AND p.ServiceId = s.ServiceId AND p.EffectiveToUtc IS NULL
  );

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 3)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(3, N'Legacy dashboard service catalog labels and entries');

