﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using App.Data;
using App.Domain.Identity;
using App.DTOs;
using App.DTOs.Account;
using App.Services.Identity.Managers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace App.Controllers
{
    [Route("role-manager")]
    public class RoleManagerController : Controller
    {
        private readonly IActionDescriptorCollectionProvider _actionDescriptor;

        private readonly AppRoleManager _roleManager;
        private readonly AppUserManager _userManager;

        private readonly ApplicationDbContext _dbContext;

        public RoleManagerController(AppRoleManager roleManager, AppUserManager userManager,
            ApplicationDbContext dbContext,
            IActionDescriptorCollectionProvider actionDescriptor)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _dbContext = dbContext;
            _actionDescriptor = actionDescriptor;
        }

        [HttpGet("", Name = "GetRoles")]
        public async Task<IActionResult> Index()
        {
            var roles = await _roleManager.Roles.ToListAsync();

            return View(roles);
        }

        [HttpGet("{roleName}/users", Name = "GetRoleUsers")]
        public async Task<IActionResult> RoleUsers(string roleName)
        {
            var role = await _roleManager.FindByNameAsync(roleName);

            if (role == null)
            {
                return NotFound();
            }

            var users = await _userManager.GetUsersInRoleAsync(roleName);

            return View(users);
        }

        [HttpGet("{roleName}/claims", Name = "GetRoleClaims")]
        public async Task<IActionResult> RoleClaims(string roleName)
        {
            var role = await _roleManager.FindByNameAsync(roleName);

            if (role == null)
            {
                return NotFound();
            }

            var claims = await _roleManager.GetClaimsAsync(role);

            return View(claims);
        }

        [HttpGet("{roleName}/permissions", Name = "GetRolePermissions")]
        public async Task<IActionResult> Permissions(string roleName)
        {
            var role = await _roleManager.Roles
                .Where(r => r.Name == roleName)
                .Include(x => x.Claims)
                .SingleOrDefaultAsync();

            if (role == null)
            {
                return NotFound();
            }

            var actions = GetDynamicPermissionActions();

            return View(new RolePermission
            {
                Role = role,
                Actions = actions
            });
        }

        [HttpPost("permissions", Name = "UpdatePermissions")]
        public async Task<IActionResult> Permissions(RolePermission model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var role = await _roleManager.Roles
                .Include(x => x.Claims)
                .SingleOrDefaultAsync(x => x.Id == model.RoleId);

            if (role == null)
            {
                return NotFound();
            }

            var selectedPermissions = model.Keys.ToList();

            var roleClaims = role.Claims
                .Where(x => x.ClaimType == "DynamicPermission")
                .Select(x => x.ClaimType)
                .ToList();

            // add new permissions
            var newPermissions = selectedPermissions.Except(roleClaims).ToList();
            foreach (var permission in newPermissions)
            {
                role.Claims.Add(new RoleClaim
                {
                    ClaimType = "DynamicPermission",
                    ClaimValue = permission,
                    GivenOn = DateTime.Now,
                });
            }

            // remove delete permissions
            var removedPermissions = roleClaims.Except(selectedPermissions).ToList();
            foreach (var permission in removedPermissions)
            {
                var roleClaim =
                    role.Claims.SingleOrDefault(x =>
                        x.ClaimType == "DynamicPermission" &&
                        x.ClaimValue == permission
                    );

                if (roleClaim != null)
                {
                    role.Claims.Remove(roleClaim);
                }
            }

            var result = await _roleManager.UpdateAsync(role);

            if (result.Succeeded)
            {
                return RedirectToRoute("GetRolePermissions", new {roleName = role.Name});
            }

            AddErrors(result);

            return View(model);
        }

        [HttpGet("new", Name = "GetCreateRole")]
        public IActionResult Create()
        {
            return View();
        }


        [HttpPost("new", Name = "PostCreateRole")]
        public async Task<IActionResult> Create(Role model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _roleManager.CreateAsync(model);

            if (result.Succeeded)
            {
                return RedirectToRoute("GetRoles");
            }

            AddErrors(result);

            return View(model);
        }


        [HttpGet("{roleName}/edit", Name = "GetEditRole")]
        public async Task<IActionResult> Edit(string roleName)
        {
            var role = await _roleManager.FindByNameAsync(roleName);

            if (role == null)
            {
                return NotFound();
            }

            return View(role);
        }

        [HttpPost("edit", Name = "PostEditRole")]
        public async Task<IActionResult> Edit(Role model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _dbContext.Roles.Update(model);

            await _dbContext.SaveChangesAsync();

            return RedirectToRoute("GetRoles");
        }


        [HttpPost("remove", Name = "PostRemoveRole")]
        public async Task<IActionResult> Remove(string roleId)
        {
            var role = await _roleManager.FindByIdAsync(roleId);

            if (role == null)
            {
                return NotFound();
            }

            _dbContext.Roles.Remove(role);

            await _dbContext.SaveChangesAsync();

            return RedirectToRoute("GetRoles");
        }

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }


        public List<ActionDto> GetDynamicPermissionActions()
        {
            var actions = new List<ActionDto>();

            var actionDescriptors = _actionDescriptor.ActionDescriptors.Items;

            foreach (var actionDescriptor in actionDescriptors)
            {
                var descriptor = (ControllerActionDescriptor) actionDescriptor;

                var hasDynamicPermission =
                    descriptor.ControllerTypeInfo
                        .GetCustomAttribute<AuthorizeAttribute>()?
                        .Policy == "DynamicPermission" ||
                    descriptor.MethodInfo.GetCustomAttribute<AuthorizeAttribute>()?
                        .Policy == "DynamicPermission";

                if (hasDynamicPermission)
                {
                    actions.Add(new ActionDto
                    {
                        AreaName = descriptor.ControllerTypeInfo.GetCustomAttribute<AreaAttribute>()?.RouteValue,
                        ActionName = descriptor.ActionName,
                        ControllerName = descriptor.ControllerName,
                        ActionDisplayName = descriptor.MethodInfo.GetCustomAttribute<DisplayAttribute>()?.Name
                    });
                }
            }

            return actions;
        }
    }
}