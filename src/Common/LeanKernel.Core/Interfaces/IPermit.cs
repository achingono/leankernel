namespace LeanKernel;

using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using LeanKernel.Entities;

public interface IPermit
{
    /// <summary>
    /// Gets the current user's ID.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// Defines a badge for a user.
    /// </summary>
    Badge Badge { get; }

    /// <summary>
    /// Gets the domain of the request.
    /// </summary>
    string HostName { get; }
}

/// <summary>
/// Defines permission checks for CRUD operations on entities.
/// </summary>
/// <typeparam name="TEntity">The type of the entity being checked.</typeparam>
public interface IPermit<TEntity> : IPermit
    where TEntity : class
{
    /// <summary>
    /// Checks if the user has permission to perform operation on a type.
    /// </summary>
    /// <returns>True if the operation is permitted; otherwise, false.</returns>
    bool Can(Operation operation);
}
