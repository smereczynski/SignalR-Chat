# Post-Deployment Manual Configuration Steps

**Status**: Current as of 2025-11-28  
**Applies to**: All environments (dev, staging, prod)

This document lists manual configuration steps required after infrastructure deployment that cannot be automated via Bicep/ARM templates.

---

## Overview

While most infrastructure is provisioned automatically via Bicep, certain Azure features require manual portal configuration due to Azure platform limitations:

| Feature | Reason | Time Required |
|---------|--------|---------------|
| **Azure Communication Services - Alphanumeric Sender ID** | No Bicep/ARM/API support | 5-10 minutes (dynamic) or 4-5 weeks (preregistered) |
| **Azure Communication Services - Email Domain Verification** | Azure Managed Domain (auto-verified) | N/A - automatic |

---

## 1. Azure Communication Services - Alphanumeric Sender ID (SMS)

### What is it?
Alphanumeric Sender ID allows SMS messages to display a custom sender name (e.g., "INTERPRES", "CONTOSO") instead of a phone number on recipient devices.

### Why manual configuration?
**Azure does not provide Bicep, ARM Template, or REST API support** for enabling Alphanumeric Sender ID. It must be configured through Azure Portal.

### Two Types

#### 1.1 Dynamic Alphanumeric Sender ID (Recommended for most cases)

**Supported countries**: Countries that don't require sender ID registration  
**Provisioning time**: Instant (allow 10 minutes before first use)  
**Use cases**: General notifications, OTP codes, alerts

**Steps:**

1. Navigate to your Azure Communication Services resource in Azure Portal
2. In the left menu, select **Alphanumeric Sender ID**
3. Select the **Dynamic** tab
4. Click **Enable Alphanumeric Sender ID**
5. Wait ~10 minutes before sending first SMS

**Verification:**
```bash
# Test SMS sending via Azure CLI
az communication sms send \
  --sender "INTERPRES" \
  --recipient "+48123456789" \
  --message "Test message from INTERPRES" \
  --connection-string "<your-acs-connection-string>"
```

#### 1.2 Preregistered Alphanumeric Sender ID (Required for UK, Ireland)

**Supported countries**: UK, Ireland (registration required by local regulations)  
**Provisioning time**: 4-5 weeks  
**Important**: UK requires registration by June 30, 2024; Ireland by October 3, 2025

**Steps:**

1. Navigate to your Azure Communication Services resource in Azure Portal
2. In the left menu, select **Alphanumeric Sender ID**
3. Select the **Preregistered** tab
4. Click **Submit an application**
5. Fill out registration form:
   - Sender ID name (e.g., "INTERPRES")
   - Use case description
   - Monthly SMS volume estimate
   - Business details
6. Submit application
7. Wait 4-5 weeks for approval notification via email

**Application URL**: [https://forms.office.com/r/pK8Jhyhtd4](https://forms.office.com/r/pK8Jhyhtd4)

### Sender ID Format Requirements

- **Length**: 1-11 characters
- **Characters allowed**: A-Z, a-z, 0-9, spaces
- **Must contain**: At least one letter
- **Examples**: 
  - ✅ `INTERPRES` (9 chars, all letters)
  - ✅ `CONTOSO` (7 chars, all letters)
  - ✅ `MyCompany` (9 chars, mixed case)
  - ✅ `Alert123` (8 chars, letters + numbers)
  - ❌ `123456` (no letters)
  - ❌ `VeryLongCompanyName` (>11 chars)

### Configuration in Application

**App Service Setting**: `Acs__SmsFrom`  
**Current Value**: `TRANSLATOR` (set via Bicep)

If you want to change the sender ID after enabling in portal:

```bash
# Update via Azure CLI
az webapp config appsettings set \
  --name <app-name> \
  --resource-group <resource-group> \
  --settings Acs__SmsFrom='INTERPRES'

# Or via Bicep (update app-service.bicep line ~160):
{
  name: 'Acs__SmsFrom'
  value: 'INTERPRES'  // Your custom sender ID
}
```

### Troubleshooting

**Issue**: Sender ID replaced with a number on recipient device  
**Cause**: Wireless carrier doesn't support alphanumeric sender IDs  
**Solution**: This is expected behavior for certain carriers to ensure delivery

**Issue**: "Enable" button not available  
**Cause**: Subscription billing address not eligible  
**Solution**: [Create support ticket](https://aka.ms/ACS-Support) with eligibility documentation

**Issue**: SMS not sent after enabling  
**Cause**: Insufficient wait time after enablement  
**Solution**: Wait minimum 10 minutes, up to 30 minutes in some cases

---

## 2. Azure Communication Services - Email Service

### What is it?
Email Service with Azure Managed Domain allows sending emails from a Microsoft-provided domain (`@<guid>.azurecomm.net`).

### Configuration Status
✅ **Fully automated via Bicep** - No manual steps required!

**What's automated:**
- Email Service resource creation (`acs-email-{baseName}-{env}`)
- Azure Managed Domain creation and verification
- Domain linking to Communication Service
- Sender email address generation (e.g., `DoNotReply@64aa6769-3f1e-480f-80cf-9a52e7219c1e.azurecomm.net`)
- App Service setting `Acs__EmailFrom` populated with sender address

**Bicep modules involved:**
- `infra/bicep/modules/communication.bicep` - Creates Email Service, Domain, and ACS
- `infra/bicep/main.bicep` - Orchestrates and passes outputs to App Service

**Verification:**
```bash
# Check Email Service provisioning
az communication email show \
  --email-service-name acs-email-interpres-dev \
  --resource-group rg-interpres-dev-plc

# Check domain status
az communication email domain show \
  --email-service-name acs-email-interpres-dev \
  --domain-name AzureManagedDomain \
  --resource-group rg-interpres-dev-plc \
  --query "properties.domainManagement"
  # Should return: "AzureManaged"

# Verify App Service setting
az webapp config appsettings list \
  --name interpres-dev-plc \
  --resource-group rg-interpres-dev-plc \
  --query "[?name=='Acs__EmailFrom'].value" -o tsv
```

**Custom Domain (Optional):**
If you want to use your own domain (e.g., `noreply@yourdomain.com`) instead of Azure Managed Domain:
1. This requires **manual DNS configuration**
2. Follow: [Add custom verified email domains](https://learn.microsoft.com/azure/communication-services/quickstarts/email/add-custom-verified-domains)
3. Update `Acs__EmailFrom` app setting to your custom email address

---

## 3. Post-Deployment Checklist

After running infrastructure deployment (`deploy` action), complete these steps:

| Step | Action | Environment | Time |
|------|--------|-------------|------|
| 1 | ✅ Verify infrastructure deployment succeeded | All | N/A |
| 2 | ✅ Verify Email Service created (automatic) | All | N/A |
| 3 | ⚠️ Enable Dynamic Alphanumeric Sender ID in Portal | All | 10 min |
| 4 | ⚠️ Submit Preregistered Sender ID application (if sending to UK/Ireland) | Prod only | 4-5 weeks |
| 5 | ✅ Test email sending via OTP login | All | 5 min |
| 6 | ✅ Test SMS sending (if enabled) | All | 5 min |
| 7 | ✅ Verify Application Insights telemetry | All | 5 min |

**Legend:**
- ✅ Automatic (no action required)
- ⚠️ Manual step required

---

## 4. Environment-Specific Notes

### Development (`dev`)
- **Alphanumeric Sender ID**: Enable dynamic sender ID for testing
- **Email Domain**: Use Azure Managed Domain (automatic)
- **SMS Volume**: Low volume, no throughput increase needed

### Staging (`staging`)
- **Alphanumeric Sender ID**: Enable dynamic sender ID
- **Preregistered Sender ID**: If testing UK/Ireland traffic, submit application
- **Email Domain**: Use Azure Managed Domain or test custom domain setup
- **SMS Volume**: Moderate volume, request throughput increase if >200 msgs/min

### Production (`prod`)
- **Alphanumeric Sender ID**: 
  - Enable dynamic sender ID (non-UK/Ireland)
  - **MUST** submit preregistered application if sending to UK/Ireland
- **Email Domain**: Consider custom verified domain for branding
- **SMS Volume**: Request throughput increase to 600+ msgs/min via support ticket
- **Monitoring**: Set up alerts for ACS failures, delivery issues

---

## 5. Related Documentation

- [Azure Communication Services - Enable Alphanumeric Sender ID](https://learn.microsoft.com/azure/communication-services/quickstarts/sms/enable-alphanumeric-sender-id)
- [Azure Communication Services - SMS FAQ](https://learn.microsoft.com/azure/communication-services/concepts/sms/sms-faq#alphanumeric-sender-id)
- [Azure Communication Services - Email Domains](https://learn.microsoft.com/azure/communication-services/quickstarts/email/add-azure-managed-domains)
- [Project Deployment Bootstrap](./bootstrap.md)
- [GitHub Actions CI/CD](./github-actions.md)

---

## 6. Support

**Azure Communication Services Issues:**
- Portal: Azure Portal → Communication Services resource → **Support + troubleshooting**
- Support ticket: [https://aka.ms/ACS-Support](https://aka.ms/ACS-Support)

**Infrastructure Issues:**
- GitHub: [Create issue](https://github.com/smereczynski/SignalR-Chat/issues)
- Discussion: [GitHub Discussions](https://github.com/smereczynski/SignalR-Chat/discussions)

---

**Last Updated**: 2025-11-28  
**Maintainer**: Project Team
