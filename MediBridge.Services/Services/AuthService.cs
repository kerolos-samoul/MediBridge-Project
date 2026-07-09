using FluentValidation;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Auth;
using MediBridge.Services.Interfaces;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Services.Config;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;

namespace MediBridge.Services.Services;

public sealed class AuthService : IAuthService
{
    private static readonly TimeSpan RegistrationAuditWindow = TimeSpan.Zero;

    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IAuthTokenService authTokenService;
    private readonly IValidator<RegisterDoctorRequestDto> registerDoctorValidator;
    private readonly IValidator<RegisterCompanyRequestDto> registerCompanyValidator;
    private readonly IValidator<LoginRequestDto> loginValidator;
    private readonly IValidator<RefreshRequestDto> refreshValidator;
    private readonly IValidator<ResubmissionRequestDto> resubmissionValidator;
    private readonly IValidator<ForgotPasswordRequestDto> forgotPasswordValidator;
    private readonly IValidator<ResetPasswordRequestDto> resetPasswordValidator;
    private readonly IValidator<VerifyContactRequestDto> verifyContactValidator;
    private readonly IValidator<RequestContactVerificationDto> requestContactVerificationValidator;
    private readonly ICurrentUserContext currentUserContext;
    private readonly ContactVerificationOptions contactVerificationOptions;
    private readonly IEmailSender emailSender;
    private readonly ILogger<AuthService> logger;

    public AuthService(
        IIdentityUnitOfWork identityUnitOfWork,
        IAuthTokenService authTokenService,
        IValidator<RegisterDoctorRequestDto> registerDoctorValidator,
        IValidator<RegisterCompanyRequestDto> registerCompanyValidator,
        IValidator<LoginRequestDto> loginValidator,
        IValidator<RefreshRequestDto> refreshValidator,
        IValidator<ResubmissionRequestDto> resubmissionValidator,
        IValidator<ForgotPasswordRequestDto> forgotPasswordValidator,
        IValidator<ResetPasswordRequestDto> resetPasswordValidator,
        IValidator<VerifyContactRequestDto> verifyContactValidator,
        IValidator<RequestContactVerificationDto> requestContactVerificationValidator,
        ICurrentUserContext currentUserContext,
        ContactVerificationOptions contactVerificationOptions,
        IEmailSender emailSender,
        ILogger<AuthService> logger)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.authTokenService = authTokenService;
        this.registerDoctorValidator = registerDoctorValidator;
        this.registerCompanyValidator = registerCompanyValidator;
        this.loginValidator = loginValidator;
        this.refreshValidator = refreshValidator;
        this.resubmissionValidator = resubmissionValidator;
        this.forgotPasswordValidator = forgotPasswordValidator;
        this.resetPasswordValidator = resetPasswordValidator;
        this.verifyContactValidator = verifyContactValidator;
        this.requestContactVerificationValidator = requestContactVerificationValidator;
        this.currentUserContext = currentUserContext;
        this.contactVerificationOptions = contactVerificationOptions;
        this.emailSender = emailSender;
        this.logger = logger;
    }

    public async Task<RegistrationResultDto> RegisterDoctorAsync(RegisterDoctorRequestDto request, CancellationToken cancellationToken = default)
    {
        await registerDoctorValidator.ValidateAndThrowAsync(request, cancellationToken);

        var normalizedEmail = request.Email.Trim();
        var normalizedPhone = request.PhoneNumber?.Trim();
        logger.LogInformation("Doctor registration starting. Email: {Email}, HasPhoneNumber: {HasPhoneNumber}", normalizedEmail, !string.IsNullOrWhiteSpace(normalizedPhone));

        if (await identityUnitOfWork.Users.ExistsByEmailAsync(normalizedEmail, cancellationToken))
        {
            throw new RegistrationConflictException("Duplicate email, phone, or license.");
        }

        if (!string.IsNullOrWhiteSpace(normalizedPhone) && await identityUnitOfWork.Users.ExistsByPhoneAsync(normalizedPhone, cancellationToken))
        {
            throw new RegistrationConflictException("Duplicate email, phone, or license.");
        }

        var operationResult = await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var now = DateTime.UtcNow;
            var user = new ApplicationUser
            {
                Email = normalizedEmail,
                PhoneNumber = normalizedPhone,
                Role = UserRole.Doctor,
                AccountStatus = AccountStatus.Pending,
                CreatedAtUtc = now,
                LastStatusChangedAtUtc = now,
                IsDeleted = false
            };

            await identityUnitOfWork.Users.AddAsync(user, request.Password, transactionCancellationToken);
            await identityUnitOfWork.Profiles.AddDoctorProfileAsync(new DoctorProfile
            {
                UserId = user.Id,
                User = user,
                Specialization = request.Specialization.Trim(),
                ExperienceYears = request.ExperienceYears,
                Location = request.Location.Trim(),
                VerificationDocumentType = request.VerificationMetadata.DocumentType.Trim(),
                VerificationOriginalFileName = request.VerificationMetadata.OriginalFileName.Trim(),
                VerificationContentType = request.VerificationMetadata.ContentType.Trim(),
                VerificationSizeBytes = request.VerificationMetadata.SizeBytes,
                VerificationReference = request.VerificationMetadata.Reference.Trim(),
                CreatedAtUtc = now
            }, transactionCancellationToken);

            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(CreateRegistrationAuditEvent(user, now), transactionCancellationToken);
            var otp = await IssueEmailVerificationOtpAsync(user, normalizedEmail, now, transactionCancellationToken);
            logger.LogInformation("Doctor registration database changes prepared. UserId: {UserId}, Role: {Role}, AccountStatus: {AccountStatus}", user.Id, user.Role, user.AccountStatus);

            return new RegistrationOperationResult(new RegistrationResultDto
            {
                UserId = user.Id,
                Role = user.Role,
                AccountStatus = user.AccountStatus
            }, user.Id, user.Role, normalizedEmail, otp);
        }, cancellationToken);

        logger.LogInformation("Doctor registration database transaction committed. UserId: {UserId}", operationResult.UserId);
        await SendEmailVerificationOtpAsync(operationResult.UserId, operationResult.Role, operationResult.Email, operationResult.Otp, cancellationToken);
        logger.LogInformation("Doctor registration completed. UserId: {UserId}, Role: {Role}, AccountStatus: {AccountStatus}", operationResult.UserId, operationResult.Role, operationResult.Result.AccountStatus);
        return operationResult.Result;
    }

    public async Task<RegistrationResultDto> RegisterCompanyAsync(RegisterCompanyRequestDto request, CancellationToken cancellationToken = default)
    {
        await registerCompanyValidator.ValidateAndThrowAsync(request, cancellationToken);

        var normalizedEmail = request.Email.Trim();
        var normalizedPhone = request.PhoneNumber?.Trim();
        var normalizedLicenseNumber = request.LicenseNumber.Trim();
        logger.LogInformation("Company registration starting. Email: {Email}, HasPhoneNumber: {HasPhoneNumber}, LicenseNumberPresent: {LicenseNumberPresent}", normalizedEmail, !string.IsNullOrWhiteSpace(normalizedPhone), !string.IsNullOrWhiteSpace(normalizedLicenseNumber));

        if (await identityUnitOfWork.Users.ExistsByEmailAsync(normalizedEmail, cancellationToken))
        {
            throw new RegistrationConflictException("Duplicate email, phone, or license.");
        }

        if (!string.IsNullOrWhiteSpace(normalizedPhone) && await identityUnitOfWork.Users.ExistsByPhoneAsync(normalizedPhone, cancellationToken))
        {
            throw new RegistrationConflictException("Duplicate email, phone, or license.");
        }

        if (await identityUnitOfWork.Profiles.CompanyLicenseExistsAsync(normalizedLicenseNumber, cancellationToken))
        {
            throw new RegistrationConflictException("Duplicate email, phone, or license.");
        }

        var operationResult = await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var now = DateTime.UtcNow;
            var user = new ApplicationUser
            {
                Email = normalizedEmail,
                PhoneNumber = normalizedPhone,
                Role = UserRole.Company,
                AccountStatus = AccountStatus.Pending,
                CreatedAtUtc = now,
                LastStatusChangedAtUtc = now,
                IsDeleted = false
            };

            await identityUnitOfWork.Users.AddAsync(user, request.Password, transactionCancellationToken);
            await identityUnitOfWork.Profiles.AddCompanyProfileAsync(new CompanyProfile
            {
                UserId = user.Id,
                User = user,
                CompanyName = request.CompanyName.Trim(),
                LicenseNumber = normalizedLicenseNumber,
                ContactName = request.ContactName.Trim(),
                VerificationDocumentType = request.VerificationMetadata.DocumentType.Trim(),
                VerificationOriginalFileName = request.VerificationMetadata.OriginalFileName.Trim(),
                VerificationContentType = request.VerificationMetadata.ContentType.Trim(),
                VerificationSizeBytes = request.VerificationMetadata.SizeBytes,
                VerificationReference = request.VerificationMetadata.Reference.Trim(),
                CreatedAtUtc = now
            }, transactionCancellationToken);

            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(CreateRegistrationAuditEvent(user, now), transactionCancellationToken);
            var otp = await IssueEmailVerificationOtpAsync(user, normalizedEmail, now, transactionCancellationToken);
            logger.LogInformation("Company registration database changes prepared. UserId: {UserId}, Role: {Role}, AccountStatus: {AccountStatus}", user.Id, user.Role, user.AccountStatus);

            return new RegistrationOperationResult(new RegistrationResultDto
            {
                UserId = user.Id,
                Role = user.Role,
                AccountStatus = user.AccountStatus
            }, user.Id, user.Role, normalizedEmail, otp);
        }, cancellationToken);

        logger.LogInformation("Company registration database transaction committed. UserId: {UserId}", operationResult.UserId);
        await SendEmailVerificationOtpAsync(operationResult.UserId, operationResult.Role, operationResult.Email, operationResult.Otp, cancellationToken);
        logger.LogInformation("Company registration completed. UserId: {UserId}, Role: {Role}, AccountStatus: {AccountStatus}", operationResult.UserId, operationResult.Role, operationResult.Result.AccountStatus);
        return operationResult.Result;
    }

    public async Task<AuthResultDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);

        var now = DateTime.UtcNow;
        var username = request.Username.Trim();
        var user = await identityUnitOfWork.Users.FindByEmailAsync(username, cancellationToken);

        if (user is null)
        {
            await RecordAuditEventAsync(CreateAuditEvent(AuthAuditEventType.LoginDenied, null, "Denied", "Invalid credentials.", now), cancellationToken);
            throw new AuthDeniedException("Invalid credentials.");
        }

        if (!await identityUnitOfWork.Users.ValidatePasswordAsync(username, request.Password, cancellationToken))
        {
            await RecordAuditEventAsync(CreateAuditEvent(AuthAuditEventType.LoginDenied, user, "Denied", "Invalid credentials.", now), cancellationToken);
            throw new AuthDeniedException("Invalid credentials.");
        }

        if (user.IsDeleted || user.AccountStatus != AccountStatus.Approved)
        {
            await RecordAuditEventAsync(CreateAuditEvent(AuthAuditEventType.LoginDenied, user, "Denied", "Account status denied.", now), cancellationToken);
            throw new AccountStatusDeniedException("Account status does not allow token issuance.");
        }

        return await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var accessToken = authTokenService.CreateAccessToken(user.Id, user.Email, user.Role.ToString());
            var (refreshPlaintext, refreshHash) = authTokenService.CreateRefreshToken();
            var refreshCredential = new RefreshCredential
            {
                UserId = user.Id,
                TokenHash = refreshHash,
                FamilyId = Guid.NewGuid().ToString("N"),
                ExpiresAtUtc = now.Add(authTokenService.RefreshTokenLifetime),
                CreatedAtUtc = now
            };

            await identityUnitOfWork.RefreshCredentials.AddAsync(refreshCredential, transactionCancellationToken);
            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                CreateAuditEvent(AuthAuditEventType.LoginSuccess, user, "Success", null, now),
                transactionCancellationToken);

            return new AuthResultDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshPlaintext,
                Role = user.Role,
                ExpiresAtUtc = now.Add(authTokenService.AccessTokenLifetime)
            };
        }, cancellationToken);
    }

    public async Task<AuthResultDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(request, cancellationToken);

        var now = DateTime.UtcNow;
        var tokenHash = authTokenService.HashToken(request.RefreshToken.Trim());

        var operationResult = await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var credential = await identityUnitOfWork.RefreshCredentials.FindByTokenHashForUpdateAsync(tokenHash, transactionCancellationToken);
            if (credential is null)
            {
                await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                    CreateAuditEvent(AuthAuditEventType.Refresh, null, "Denied", "Invalid refresh token.", now),
                    transactionCancellationToken);

                return RefreshOperationResult.AuthDenied("Invalid refresh token.");
            }

            if (credential.RevokedAtUtc is not null)
            {
                await identityUnitOfWork.RefreshCredentials.RevokeFamilyAsync(credential.FamilyId, "ReuseDetected", transactionCancellationToken);
                await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                    CreateAuditEvent(AuthAuditEventType.RefreshReuseDetected, await identityUnitOfWork.Users.FindByIdAsync(credential.UserId, transactionCancellationToken),
                        "ReuseDetected", "Refresh token reuse detected.", now),
                    transactionCancellationToken);

                return RefreshOperationResult.ReuseDetected();
            }

            if (credential.ExpiresAtUtc < now)
            {
                await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                    CreateAuditEvent(AuthAuditEventType.Refresh, null, "Denied", "Invalid refresh token.", now),
                    transactionCancellationToken);

                return RefreshOperationResult.AuthDenied("Invalid refresh token.");
            }

            var user = await identityUnitOfWork.Users.FindByIdAsync(credential.UserId, transactionCancellationToken);
            if (user is null || user.IsDeleted || user.AccountStatus != AccountStatus.Approved)
            {
                await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                    CreateAuditEvent(AuthAuditEventType.Refresh, user, "Denied", "Account status denied.", now),
                    transactionCancellationToken);

                return RefreshOperationResult.AuthDenied("Account status does not allow token issuance.");
            }

            credential.RevokedAtUtc = now;
            credential.RevocationReason = "Rotated";

            var (refreshPlaintext, refreshHash) = authTokenService.CreateRefreshToken();
            credential.ReplacedByTokenHash = refreshHash;

            var replacement = new RefreshCredential
            {
                UserId = user.Id,
                TokenHash = refreshHash,
                FamilyId = credential.FamilyId,
                ExpiresAtUtc = now.Add(authTokenService.RefreshTokenLifetime),
                CreatedAtUtc = now
            };

            await identityUnitOfWork.RefreshCredentials.AddAsync(replacement, transactionCancellationToken);
            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                CreateAuditEvent(AuthAuditEventType.Refresh, user, "Success", null, now),
                transactionCancellationToken);

            var accessToken = authTokenService.CreateAccessToken(user.Id, user.Email, user.Role.ToString());

            return RefreshOperationResult.Success(new AuthResultDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshPlaintext,
                Role = user.Role,
                ExpiresAtUtc = now.Add(authTokenService.AccessTokenLifetime)
            });
        }, cancellationToken);

        if (operationResult.IsReuseDetected)
        {
            throw new RefreshReuseDetectedException("Refresh token reuse detected.");
        }

        if (operationResult.AuthDeniedMessage is not null)
        {
            throw new AuthDeniedException(operationResult.AuthDeniedMessage);
        }

        return operationResult.AuthResult ?? throw new InvalidOperationException("Refresh operation completed without a result.");
    }

    public async Task LogoutAsync(string userId, RefreshRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new AuthDeniedException("Invalid credentials.");
        }

        var now = DateTime.UtcNow;
        var tokenHash = authTokenService.HashToken(request.RefreshToken.Trim());

        await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var credential = await identityUnitOfWork.RefreshCredentials.FindByTokenHashForUpdateAsync(tokenHash, transactionCancellationToken);
            if (credential is null || credential.UserId != userId || credential.ExpiresAtUtc < now)
            {
                throw new AuthDeniedException("Invalid credentials.");
            }

            await identityUnitOfWork.RefreshCredentials.RevokeFamilyAsync(credential.FamilyId, "Logout", transactionCancellationToken);

            var user = await identityUnitOfWork.Users.FindByIdAsync(userId, transactionCancellationToken);
            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                CreateAuditEvent(AuthAuditEventType.Logout, user, "Success", null, now),
                transactionCancellationToken);
        }, cancellationToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        await forgotPasswordValidator.ValidateAndThrowAsync(request, cancellationToken);

        var contact = request.Contact.Trim();
        var user = await identityUnitOfWork.Users.FindByContactAsync(contact, cancellationToken);
        if (user is null || user.IsDeleted)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var (flow, plaintextToken) = await identityUnitOfWork.ExecuteInTransactionAsync(
            async transactionCancellationToken =>
            {
                var tokenResult = authTokenService.CreateOneTimeToken();
                var resetFlow = new PasswordResetFlow
                {
                    UserId = user.Id,
                    TokenHash = tokenResult.TokenHash,
                    ExpiresAtUtc = now.AddHours(1),
                    CreatedAtUtc = now,
                    RequestCorrelationId = currentUserContext.CorrelationId ?? Guid.NewGuid().ToString("N")
                };
                await identityUnitOfWork.PasswordResetFlows.AddAsync(resetFlow, transactionCancellationToken);
                await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                    CreateAuditEvent(AuthAuditEventType.PasswordResetRequested, user, "Success", null, now),
                    transactionCancellationToken);
                return (resetFlow, tokenResult.PlaintextToken);
            },
            cancellationToken);

        await SendPasswordResetTokenAsync(
            user.Id,
            user.Role,
            user.Email,
            plaintextToken,
            flow.Id,
            now,
            cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        await resetPasswordValidator.ValidateAndThrowAsync(request, cancellationToken);

        var tokenHash = authTokenService.HashToken(request.ResetToken.Trim());
        var now = DateTime.UtcNow;

        await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var flow = await identityUnitOfWork.PasswordResetFlows.FindUnconsumedByTokenHashAsync(tokenHash, transactionCancellationToken)
                ?? throw new AuthDeniedException("Invalid credentials.");

            if (string.IsNullOrWhiteSpace(flow.UserId))
            {
                throw new AuthDeniedException("Invalid credentials.");
            }

            var user = await identityUnitOfWork.Users.FindByIdAsync(flow.UserId, transactionCancellationToken)
                ?? throw new AuthDeniedException("Invalid credentials.");

            if (user.IsDeleted)
            {
                throw new AuthDeniedException("Invalid credentials.");
            }

            await identityUnitOfWork.Users.ResetPasswordAsync(user.Id, request.NewPassword, transactionCancellationToken);
            await identityUnitOfWork.PasswordResetFlows.MarkConsumedAsync(flow, now, transactionCancellationToken);
            await identityUnitOfWork.RefreshCredentials.RevokeByUserAsync(user.Id, "PasswordChanged", transactionCancellationToken);
            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                CreateAuditEvent(AuthAuditEventType.PasswordResetCompleted, user, "Success", null, now),
                transactionCancellationToken);
        }, cancellationToken);
    }

    public async Task VerifyContactAsync(VerifyContactRequestDto request, CancellationToken cancellationToken = default)
    {
        await verifyContactValidator.ValidateAndThrowAsync(request, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Otp))
        {
            await VerifyEmailOtpAsync(request, cancellationToken);
            return;
        }

        var tokenHash = authTokenService.HashToken(request.VerificationToken.Trim());
        var now = DateTime.UtcNow;

        await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var flow = await identityUnitOfWork.ContactVerificationFlows.FindUnconsumedByTokenHashAsync(tokenHash, transactionCancellationToken)
                ?? throw new ValidationException("Invalid verification token.");

            if (flow.Channel != request.Channel)
            {
                throw new ValidationException("Invalid verification token.");
            }

            var user = await identityUnitOfWork.Users.FindByIdAsync(flow.UserId, transactionCancellationToken)
                ?? throw new ValidationException("Invalid verification token.");

            if (request.Channel == ContactVerificationChannel.Email)
            {
                user.EmailVerified = true;
            }
            else
            {
                user.PhoneVerified = true;
            }

            await identityUnitOfWork.Users.UpdateAsync(user, transactionCancellationToken);
            await identityUnitOfWork.ContactVerificationFlows.MarkConsumedAsync(flow, now, transactionCancellationToken);
            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                CreateAuditEvent(AuthAuditEventType.ContactVerificationCompleted, user, "Success", null, now),
                transactionCancellationToken);
        }, cancellationToken);
    }

    public async Task<RegistrationResultDto> ResubmitRegistrationAsync(ResubmissionRequestDto request, CancellationToken cancellationToken = default)
    {
        await resubmissionValidator.ValidateAndThrowAsync(request, cancellationToken);

        var tokenHash = authTokenService.HashToken(request.ResubmissionToken.Trim());
        var now = DateTime.UtcNow;

        return await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var resubmissionToken = await identityUnitOfWork.AccountResubmissionTokens.FindUnconsumedByTokenHashForUpdateAsync(tokenHash, transactionCancellationToken)
                ?? throw new AccountStatusDeniedException("Account status denied.");

            var user = await identityUnitOfWork.Users.FindByIdAsync(resubmissionToken.UserId, transactionCancellationToken)
                ?? throw new AccountStatusDeniedException("Account status denied.");

            if (user.IsDeleted || user.AccountStatus != AccountStatus.Rejected || user.Role is not (UserRole.Doctor or UserRole.Company))
            {
                throw new AccountStatusDeniedException("Account status denied.");
            }

            var updatedProfileFields = await UpdateResubmittedProfileAsync(user.Id, user.Role, request, now, transactionCancellationToken);
            var updatedVerificationMetadata = JsonSerializer.Serialize(request.VerificationMetadata);

            await identityUnitOfWork.AccountResubmissionTokens.MarkConsumedAsync(resubmissionToken, now, transactionCancellationToken);
            await identityUnitOfWork.AccountResubmissions.AddAsync(new AccountResubmission
            {
                UserId = user.Id,
                SubmittedAtUtc = now,
                UpdatedProfileFields = updatedProfileFields,
                UpdatedVerificationMetadata = updatedVerificationMetadata
            }, transactionCancellationToken);

            user.AccountStatus = AccountStatus.Pending;
            user.ApprovedAtUtc = null;
            user.LastStatusChangedAtUtc = now;
            await identityUnitOfWork.Users.UpdateAsync(user, transactionCancellationToken);

            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(new AuthenticationAuditEvent
            {
                EventType = AuthAuditEventType.AccountResubmission,
                ActorUserId = null,
                TargetUserId = user.Id,
                Role = user.Role,
                Outcome = "Success",
                Reason = null,
                CorrelationId = currentUserContext.CorrelationId ?? Guid.NewGuid().ToString("N"),
                CreatedAtUtc = now
            }, transactionCancellationToken);

            return new RegistrationResultDto
            {
                UserId = user.Id,
                Role = user.Role,
                AccountStatus = user.AccountStatus
            };
        }, cancellationToken);
    }

    private async Task<string> UpdateResubmittedProfileAsync(
        string userId,
        UserRole role,
        ResubmissionRequestDto request,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var changedFields = new Dictionary<string, object?>();

        if (role == UserRole.Doctor)
        {
            var profile = await identityUnitOfWork.Profiles.FindDoctorProfileByUserIdAsync(userId, cancellationToken)
                ?? throw new AccountStatusDeniedException("Account status denied.");

            if (!string.IsNullOrWhiteSpace(request.Specialization))
            {
                profile.Specialization = request.Specialization.Trim();
                changedFields[nameof(request.Specialization)] = profile.Specialization;
            }

            if (request.ExperienceYears.HasValue)
            {
                profile.ExperienceYears = request.ExperienceYears.Value;
                changedFields[nameof(request.ExperienceYears)] = profile.ExperienceYears;
            }

            if (!string.IsNullOrWhiteSpace(request.Location))
            {
                profile.Location = request.Location.Trim();
                changedFields[nameof(request.Location)] = profile.Location;
            }

            ApplyVerificationMetadata(profile, request.VerificationMetadata);
            profile.UpdatedAtUtc = updatedAtUtc;
            changedFields[nameof(request.VerificationMetadata)] = "Updated";
        }
        else if (role == UserRole.Company)
        {
            var profile = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(userId, cancellationToken)
                ?? throw new AccountStatusDeniedException("Account status denied.");

            if (!string.IsNullOrWhiteSpace(request.CompanyName))
            {
                profile.CompanyName = request.CompanyName.Trim();
                changedFields[nameof(request.CompanyName)] = profile.CompanyName;
            }

            if (!string.IsNullOrWhiteSpace(request.LicenseNumber))
            {
                var normalizedLicenseNumber = request.LicenseNumber.Trim();
                if (!string.Equals(normalizedLicenseNumber, profile.LicenseNumber, StringComparison.Ordinal)
                    && await identityUnitOfWork.Profiles.CompanyLicenseExistsAsync(normalizedLicenseNumber, cancellationToken))
                {
                    throw new RegistrationConflictException("Duplicate email, phone, or license.");
                }

                profile.LicenseNumber = normalizedLicenseNumber;
                changedFields[nameof(request.LicenseNumber)] = profile.LicenseNumber;
            }

            if (!string.IsNullOrWhiteSpace(request.ContactName))
            {
                profile.ContactName = request.ContactName.Trim();
                changedFields[nameof(request.ContactName)] = profile.ContactName;
            }

            ApplyVerificationMetadata(profile, request.VerificationMetadata);
            profile.UpdatedAtUtc = updatedAtUtc;
            changedFields[nameof(request.VerificationMetadata)] = "Updated";
        }

        return JsonSerializer.Serialize(changedFields);
    }

    private static void ApplyVerificationMetadata(DoctorProfile profile, VerificationMetadataDto metadata)
    {
        profile.VerificationDocumentType = metadata.DocumentType.Trim();
        profile.VerificationOriginalFileName = metadata.OriginalFileName.Trim();
        profile.VerificationContentType = metadata.ContentType.Trim();
        profile.VerificationSizeBytes = metadata.SizeBytes;
        profile.VerificationReference = metadata.Reference.Trim();
    }

    private static void ApplyVerificationMetadata(CompanyProfile profile, VerificationMetadataDto metadata)
    {
        profile.VerificationDocumentType = metadata.DocumentType.Trim();
        profile.VerificationOriginalFileName = metadata.OriginalFileName.Trim();
        profile.VerificationContentType = metadata.ContentType.Trim();
        profile.VerificationSizeBytes = metadata.SizeBytes;
        profile.VerificationReference = metadata.Reference.Trim();
    }

    private AuthenticationAuditEvent CreateRegistrationAuditEvent(ApplicationUser user, DateTime createdAtUtc)
    {
        return new AuthenticationAuditEvent
        {
            EventType = AuthAuditEventType.Registration,
            ActorUserId = null,
            TargetUserId = user.Id,
            Role = user.Role,
            Outcome = "Success",
            Reason = null,
            CorrelationId = currentUserContext.CorrelationId ?? Guid.NewGuid().ToString("N"),
            CreatedAtUtc = createdAtUtc
        };
    }

    private async Task<string> IssueEmailVerificationOtpAsync(
        ApplicationUser user,
        string email,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var otp = CreateNumericOtp(contactVerificationOptions.OtpLength);
        var flow = new ContactVerificationFlow
        {
            UserId = user.Id,
            Channel = ContactVerificationChannel.Email,
            DestinationHash = authTokenService.HashToken(email),
            TokenHash = authTokenService.HashToken(otp),
            ExpiresAtUtc = now.AddMinutes(contactVerificationOptions.ExpirationMinutes),
            LastSentAtUtc = now,
            CreatedAtUtc = now
        };

        await identityUnitOfWork.ContactVerificationFlows.AddAsync(flow, cancellationToken);
        logger.LogInformation(
            "Email OTP contact verification flow created. FlowId: {FlowId}, UserId: {UserId}, Channel: {Channel}, ExpiresAtUtc: {ExpiresAtUtc}, LastSentAtUtc: {LastSentAtUtc}",
            flow.Id,
            flow.UserId,
            flow.Channel,
            flow.ExpiresAtUtc,
            flow.LastSentAtUtc);
        await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
            CreateAuditEvent(AuthAuditEventType.ContactVerificationRequested, user, "Success", null, now),
            cancellationToken);
        return otp;
    }

    private async Task VerifyEmailOtpAsync(VerifyContactRequestDto request, CancellationToken cancellationToken)
    {
        if (request.Channel != ContactVerificationChannel.Email)
        {
            throw new ValidationException("Invalid verification token.");
        }

        var normalizedEmail = request.Email.Trim();
        var tokenHash = authTokenService.HashToken(request.Otp.Trim());
        var destinationHash = authTokenService.HashToken(normalizedEmail);
        var now = DateTime.UtcNow;
        var verificationFailed = false;

        await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var flow = await identityUnitOfWork.ContactVerificationFlows.FindUnconsumedByTokenHashAsync(tokenHash, transactionCancellationToken);
            if (flow is null)
            {
                await RecordFailedEmailOtpAttemptAsync(normalizedEmail, destinationHash, now, transactionCancellationToken);
                verificationFailed = true;
                return;
            }

            if (flow.Channel != ContactVerificationChannel.Email || flow.DestinationHash != destinationHash)
            {
                await RecordFailedEmailOtpAttemptAsync(normalizedEmail, destinationHash, now, transactionCancellationToken);
                verificationFailed = true;
                return;
            }

            var user = await identityUnitOfWork.Users.FindByIdAsync(flow.UserId, transactionCancellationToken)
                ?? throw new ValidationException("Invalid verification token.");

            if (!string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Invalid verification token.");
            }

            user.EmailVerified = true;
            await identityUnitOfWork.Users.UpdateAsync(user, transactionCancellationToken);
            await identityUnitOfWork.ContactVerificationFlows.MarkConsumedAsync(flow, now, transactionCancellationToken);
            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                CreateAuditEvent(AuthAuditEventType.ContactVerificationCompleted, user, "Success", null, now),
                transactionCancellationToken);
        }, cancellationToken);

        if (verificationFailed)
        {
            throw new ValidationException("Invalid verification token.");
        }
    }

    private async Task RecordFailedEmailOtpAttemptAsync(
        string normalizedEmail,
        string destinationHash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var user = await identityUnitOfWork.Users.FindByEmailAsync(normalizedEmail, cancellationToken)
            ?? throw new ValidationException("Invalid verification token.");

        var flows = await identityUnitOfWork.ContactVerificationFlows.ListUnconsumedByUserDestinationAsync(
            user.Id,
            ContactVerificationChannel.Email,
            destinationHash,
            cancellationToken);

        var activeFlow = flows.FirstOrDefault(flow => flow.SupersededAtUtc is null
                                                     && flow.ExpiresAtUtc >= now
                                                     && flow.MaxAttemptsReachedAtUtc is null)
            ?? throw new ValidationException("Invalid verification token.");

        activeFlow.FailedAttemptCount++;
        if (activeFlow.FailedAttemptCount >= contactVerificationOptions.MaxAttempts)
        {
            activeFlow.MaxAttemptsReachedAtUtc = now;
            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
                CreateAuditEvent(AuthAuditEventType.ContactVerificationMaxAttemptsReached, user, "Denied", "Max attempts reached.", now),
                cancellationToken);
        }

        await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(
            CreateAuditEvent(AuthAuditEventType.ContactVerificationFailed, user, "Denied", "Invalid verification token.", now),
            cancellationToken);
    }

    private async Task SendEmailVerificationOtpAsync(string userId, UserRole role, string registeredEmail, string otp, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Email OTP delivery preparing. UserId: {UserId}, RegisteredEmail: {RegisteredEmail}, OverrideEnabled: {OverrideEnabled}, OverrideRecipientConfigured: {OverrideRecipientConfigured}",
            userId,
            registeredEmail,
            contactVerificationOptions.AllowOverrideRecipientEmail,
            !string.IsNullOrWhiteSpace(contactVerificationOptions.OverrideRecipientEmail));

        var recipientEmail = contactVerificationOptions.AllowOverrideRecipientEmail
            ? contactVerificationOptions.OverrideRecipientEmail?.Trim()
            : registeredEmail;

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            recipientEmail = registeredEmail;
        }

        logger.LogInformation(
            "Email OTP delivery recipient resolved. UserId: {UserId}, RegisteredEmail: {RegisteredEmail}, RecipientEmail: {RecipientEmail}",
            userId,
            registeredEmail,
            recipientEmail);

        try
        {
            await emailSender.SendAsync(new EmailMessageDto
            {
                RecipientEmail = recipientEmail,
                Subject = "MediBridge email verification OTP",
                Body = $"""
                    Your MediBridge email verification OTP is: {otp}

                    Registered email: {registeredEmail}
                    This code expires in {contactVerificationOptions.ExpirationMinutes} minutes.
                    """
            }, cancellationToken);

            await RecordAuditEventAsync(new AuthenticationAuditEvent
            {
                EventType = AuthAuditEventType.ContactVerificationSent,
                ActorUserId = null,
                TargetUserId = userId,
                Role = role,
                Outcome = "Success",
                Reason = null,
                CorrelationId = currentUserContext.CorrelationId ?? Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow
            }, cancellationToken);

            logger.LogInformation(
                "Email OTP delivery succeeded. UserId: {UserId}, RecipientEmail: {RecipientEmail}",
                userId,
                recipientEmail);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Email OTP delivery failed. UserId: {UserId}, RegisteredEmail: {RegisteredEmail}, RecipientEmail: {RecipientEmail}",
                userId,
                registeredEmail,
                recipientEmail);

            await RecordAuditEventAsync(new AuthenticationAuditEvent
            {
                EventType = AuthAuditEventType.ContactVerificationFailed,
                ActorUserId = null,
                TargetUserId = userId,
                Role = role,
                Outcome = "Denied",
                Reason = "Email delivery failed.",
                CorrelationId = currentUserContext.CorrelationId ?? Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow
            }, cancellationToken);
        }
    }

    private async Task SendPasswordResetTokenAsync(
        string userId,
        UserRole role,
        string registeredEmail,
        string plaintextToken,
        string flowId,
        DateTime requestedAtUtc,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Password reset email delivery preparing. UserId: {UserId}, FlowId: {FlowId}, RegisteredEmail: {RegisteredEmail}",
            userId,
            flowId,
            registeredEmail);

        try
        {
            await emailSender.SendAsync(new EmailMessageDto
            {
                RecipientEmail = registeredEmail,
                Subject = "MediBridge password reset",
                Body = $"""
                    A password reset was requested for your MediBridge account.

                    Registered email: {registeredEmail}
                    Requested at (UTC): {requestedAtUtc:O}

                    Use the reset token below to set a new password. This token expires in 1 hour and can only be used once.

                    {plaintextToken}
                    """
            }, cancellationToken);

            await RecordAuditEventAsync(new AuthenticationAuditEvent
            {
                EventType = AuthAuditEventType.PasswordResetSent,
                ActorUserId = userId,
                TargetUserId = userId,
                Role = role,
                Outcome = "Success",
                Reason = null,
                CorrelationId = currentUserContext.CorrelationId ?? Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow
            }, cancellationToken);

            logger.LogInformation(
                "Password reset email delivery succeeded. UserId: {UserId}, FlowId: {FlowId}, RecipientEmail: {RecipientEmail}",
                userId,
                flowId,
                registeredEmail);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Password reset email delivery failed. UserId: {UserId}, FlowId: {FlowId}, RegisteredEmail: {RegisteredEmail}",
                userId,
                flowId,
                registeredEmail);

            await RecordAuditEventAsync(new AuthenticationAuditEvent
            {
                EventType = AuthAuditEventType.PasswordResetSendFailed,
                ActorUserId = userId,
                TargetUserId = userId,
                Role = role,
                Outcome = "Denied",
                Reason = "Email delivery failed.",
                CorrelationId = currentUserContext.CorrelationId ?? Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow
            }, cancellationToken);
        }
    }

    public async Task RequestContactVerificationAsync(RequestContactVerificationDto request, CancellationToken cancellationToken = default)
    {
        await requestContactVerificationValidator.ValidateAndThrowAsync(request, cancellationToken);

        var normalizedEmail = request.Email.Trim();
        var destinationHash = authTokenService.HashToken(normalizedEmail);
        var now = DateTime.UtcNow;
        string? otp = null;

        await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var user = await identityUnitOfWork.Users.FindByEmailAsync(normalizedEmail, transactionCancellationToken)
                ?? throw new ValidationException("Invalid contact verification request.");

            if (user.IsDeleted || user.EmailVerified)
            {
                throw new ValidationException("Invalid contact verification request.");
            }

            var flows = await identityUnitOfWork.ContactVerificationFlows.ListUnconsumedByUserDestinationAsync(
                user.Id,
                ContactVerificationChannel.Email,
                destinationHash,
                transactionCancellationToken);

            var latestFlow = flows.FirstOrDefault();
            if (latestFlow is not null && latestFlow.LastSentAtUtc.AddSeconds(contactVerificationOptions.ResendCooldownSeconds) > now)
            {
                throw new ContactVerificationRateLimitedException("Email verification resend is temporarily rate limited.");
            }

            foreach (var flow in flows.Where(flow => flow.ExpiresAtUtc >= now))
            {
                flow.SupersededAtUtc = now;
            }

            otp = await IssueEmailVerificationOtpAsync(user, normalizedEmail, now, transactionCancellationToken);
        }, cancellationToken);

        var deliveryUser = await identityUnitOfWork.Users.FindByEmailAsync(normalizedEmail, cancellationToken)
            ?? throw new ValidationException("Invalid contact verification request.");
        await SendEmailVerificationOtpAsync(
            deliveryUser.Id,
            deliveryUser.Role,
            normalizedEmail,
            otp ?? throw new InvalidOperationException("OTP was not created."),
            cancellationToken);
    }

    private static string CreateNumericOtp(int length)
    {
        var minimum = (int)Math.Pow(10, length - 1);
        var maximumExclusive = (int)Math.Pow(10, length);
        return RandomNumberGenerator.GetInt32(minimum, maximumExclusive).ToString("D" + length, System.Globalization.CultureInfo.InvariantCulture);
    }

    private AuthenticationAuditEvent CreateAuditEvent(
        AuthAuditEventType eventType,
        ApplicationUser? user,
        string outcome,
        string? reason,
        DateTime createdAtUtc)
    {
        return new AuthenticationAuditEvent
        {
            EventType = eventType,
            ActorUserId = user?.Id,
            TargetUserId = user?.Id,
            Role = user?.Role,
            Outcome = outcome,
            Reason = reason,
            CorrelationId = currentUserContext.CorrelationId ?? Guid.NewGuid().ToString("N"),
            CreatedAtUtc = createdAtUtc
        };
    }

    private Task RecordAuditEventAsync(AuthenticationAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        return identityUnitOfWork.ExecuteInTransactionAsync(
            transactionCancellationToken => identityUnitOfWork.AuthenticationAuditEvents.AddAsync(auditEvent, transactionCancellationToken),
            cancellationToken);
    }

    private sealed record RefreshOperationResult(AuthResultDto? AuthResult, bool IsReuseDetected, string? AuthDeniedMessage)
    {
        public static RefreshOperationResult ReuseDetected()
        {
            return new RefreshOperationResult(null, true, null);
        }

        public static RefreshOperationResult AuthDenied(string message)
        {
            return new RefreshOperationResult(null, false, message);
        }

        public static RefreshOperationResult Success(AuthResultDto result)
        {
            return new RefreshOperationResult(result, false, null);
        }
    }

    private sealed record RegistrationOperationResult(RegistrationResultDto Result, string UserId, UserRole Role, string Email, string Otp);
}
